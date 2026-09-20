using Assets.Bluetooth;
using Assets.Device;
using Microsoft.Win32.SafeHandles;
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Levelstep
{
    /// <summary>
    /// Bridges the WitMotion WT9011DCL balance sensor into Unity for the balance board.
    /// Primary path: the white USB adapter relays live sensor data over a COM port, so we read
    /// and parse its 0x55 0x61 frames on a worker thread (Windows; same protocol the vendor's
    /// MiniIMU uses). A BLE path remains in this file but is dormant unless re-enabled.
    /// Live angle/rate data is snapped into main-thread-safe values, and a calibratable "flat"
    /// reference keeps gameplay reading tilt relative to the board's rest pose.
    /// 
    /// Keyboard (play mode):
    ///   C = connect / reconnect serial        B = calibrate flat (averages a short sample)
    ///   While Simulation is enabled, the arrow keys + space inject fake tilt.
    /// </summary>
    public class BalanceBoardBridge : MonoBehaviour
    {
        [Tooltip("Optional config asset. If unassigned, the serialized values below are used.")]
        public LevelstepClimbingConfig config;

        [Tooltip("Name prefix used to identify the sensor while scanning (e.g. 'WT901BLE67').")]
        public string deviceNameFilter = "WT901BLE67";

        [Tooltip("Log scanning/discovery progress (scan start, every device seen, hints). Leave ON while setting up; untick for a quiet fair day.")]
        public bool debugLogging = true;

        [Tooltip("COM port the white USB adapter shows up as (e.g. 'COM5'). Leave empty to auto-detect by listening for WT901 frames on every port.")]
        public string portName = "";

        [Tooltip("Baud rate of the serial link (matches the vendor software).")]
        public int baudRate = 115200;

        [Tooltip("Seconds to listen on each COM port during auto-detection.")]
        public float serialProbeSeconds = 2f;

        [Tooltip("Automatically start scanning and connect when play starts.")]
        public bool autoConnect = true;

        [Tooltip("Automatically calibrate flat shortly after connecting (averaged over calibrateSamples).")]
        public float autoCalibrateSeconds = 1f;

        [Tooltip("Number of angle samples averaged while calibrating the flat reference.")]
        public int calibrateSamples = 20;

        public static BalanceBoardBridge Instance { get; private set; }

        public bool IsConnected
        {
            get
            {
                if (m_Device != null && m_Device.isOpen) return true;
                return m_SerialState == kSerialConnected;
            }
        }
        public bool IsScanning { get; private set; }
        public bool IsCalibrated { get; private set; }

        /// <summary>Tilt relative to the calibrated flat reference, in degrees (X, Y).</summary>
        public Vector2 TiltDeg { get; private set; }

        /// <summary>Angular rate in degrees/second (X, Y), sensor frame.</summary>
        public Vector2 AngularRateDegS { get; private set; }

        public float BatteryPercent { get; private set; }

        /// <summary>Seconds since the last live sensor packet (float.MaxValue when never connected).</summary>
        public float SignalAge
        {
            get
            {
                if (m_LastPacketTime <= 0f) return float.MaxValue;
                return Mathf.Max(0f, Time.time - m_LastPacketTime);
            }
        }

        /// <summary>
        /// When enabled the live hardware is ignored and rotation is driven by <see cref="SimRawTilt"/>
        /// and <see cref="SimRate"/>. Used for keyboard-simulated playtests and headless sanity checks.
        /// </summary>
        public bool SimulateEnabled { get; set; }

        /// <summary>Simulated raw tilt (what the sensor would report before calibration).</summary>
        public Vector2 SimRawTilt;

        /// <summary>Simulated angular rate.</summary>
        public Vector2 SimRate;

        DeviceModel m_Device;

        readonly object m_SnapLock = new object();
        Vector2 m_SnapAngle;
        Vector2 m_SnapRate;
        float m_SnapBattery;
        bool m_SnapUpdated;

        readonly object m_ScanLock = new object();
        string m_PendingName;
        string m_PendingId;
        bool m_ScanFinished;

        float m_ScanStartTime;
        bool m_ScanHintLogged;

        const int kSerialOff = 0;
        const int kSerialProbing = 1;
        const int kSerialConnected = 2;

        volatile int m_SerialState;
        volatile bool m_SerialStop;
        Thread m_SerialThread;
        SafeFileHandle m_SerialHandle;
        string m_SerialPortName;
        float m_SerialConnectedAt;
        bool m_SerialNoDataWarned;

        float m_LastPacketTime;

        float m_DeviceOpenedAt;
        bool m_NoDataWarned;

        Vector2 m_DeviceAngle;
        Vector2 m_DeviceRate;

        Vector2 m_CalibSum;
        int m_CalibCount;
        bool m_Calibrating;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // A duplicate bridge spawned by a scene reload (P reset). The surviving
                // instance keeps the serial connection + calibration; destroy this copy
                // without touching the sensor data.
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Disconnect();
        }

        void Start()
        {
            if (config != null)
            {
                if (!string.IsNullOrEmpty(config.balanceDeviceNameFilter)) deviceNameFilter = config.balanceDeviceNameFilter;
                if (!string.IsNullOrEmpty(config.balanceSerialPortName)) portName = config.balanceSerialPortName;
                autoConnect = config.balanceAutoConnect;
                autoCalibrateSeconds = config.balanceAutoCalibrateSeconds;
            }
            if (autoConnect) Connect();
        }

        void Update()
        {
            HandleKeyboard();
            ConnectFromPendingScan();
            CheckNoData();
            CheckScanHint();
            TrackSerialState();

            if (IsConnected && !IsCalibrated && !m_Calibrating && autoCalibrateSeconds > 0f && m_LastPacketTime > 0f)
                CalibrateFlat();

            Refresh();
        }

        // Main thread: watches the worker thread's state transitions and clears the handshake flags.
        void TrackSerialState()
        {
            if (m_SerialState == kSerialConnected)
            {
                if (m_SerialConnectedAt <= 0f)
                {
                    m_SerialConnectedAt = Time.time;
                    m_SerialNoDataWarned = false;
                    Debug.Log("[Balance] Serial connected on " + m_SerialPortName + " with live WT901 data. Press B to calibrate flat.");
                }
                return;
            }

            if (m_SerialState != kSerialProbing)
            {
                m_SerialConnectedAt = 0f;
                m_SerialNoDataWarned = false;
            }
        }

        void CheckScanHint()
        {
            if (!debugLogging || !IsScanning || m_ScanHintLogged || m_ScanStartTime <= 0f) return;
            if (Time.time - m_ScanStartTime < 15f) return;
            m_ScanHintLogged = true;
            Debug.Log("[Balance] Still scanning for '" + deviceNameFilter + "'. Is the sensor powered on, close to the laptop, and unpaired in Windows Bluetooth settings?");
        }

        void CheckNoData()
        {
            if (m_Device != null && m_Device.isOpen)
            {
                if (m_NoDataWarned || m_DeviceOpenedAt <= 0f || Time.time - m_DeviceOpenedAt < 3f) return;
                if (m_LastPacketTime > m_DeviceOpenedAt) return;
                m_NoDataWarned = true;
                Debug.LogWarning("[Balance] Connected over BLE but no sensor data arrived. This sensor may not expose a usable notify service - hold SPACE to fake the meter instead.");
                return;
            }

            if (m_SerialState != kSerialConnected || m_SerialNoDataWarned || m_SerialConnectedAt <= 0f || Time.time - m_SerialConnectedAt < 3f) return;
            if (m_LastPacketTime > m_SerialConnectedAt) return;
            m_SerialNoDataWarned = true;
            Debug.LogWarning("[Balance] Serial alive but no WT901 frames are being received. Check the sensor is on and linked to the white adapter - hold SPACE to fake the meter instead.");
        }

        /// <summary>
        /// Pulls the latest hardware snapshot (or simulation input) and recomputes the calibrated
        /// tilt / rate. Called every frame by Update and also by consumers such as the balance meter
        /// so headless tests get fresh values without a running game loop.
        /// </summary>
        public void Refresh()
        {
            lock (m_SnapLock)
            {
                if (m_SnapUpdated)
                {
                    m_SnapUpdated = false;
                    m_LastPacketTime = Time.time;
                    m_DeviceAngle = m_SnapAngle;
                    m_DeviceRate = m_SnapRate;
                    BatteryPercent = m_SnapBattery;
                }
            }

            Vector2 raw = SimulateEnabled ? SimRawTilt : m_DeviceAngle;
            if (m_CalibCount > 0)
            {
                m_CalibSum += raw;
                m_CalibCount++;
                if (m_CalibCount >= calibrateSamples)
                {
                    Vector2 offset = m_CalibSum / calibrateSamples;
                    m_CalibCount = 0;
                    SetCalibrated(offset);
                }
            }

            TiltDeg = Unwrap(raw - m_CalibOffset);
            AngularRateDegS = SimulateEnabled ? SimRate : m_DeviceRate;
        }

        Vector2 m_CalibOffset;

        void SetCalibrated(Vector2 offset)
        {
            m_CalibOffset = offset;
            m_Calibrating = false;
            IsCalibrated = true;
            Debug.Log($"[Balance] Calibrated flat reference at {offset.x:F1}°, {offset.y:F1}°.");
        }

        static Vector2 Unwrap(Vector2 v)
        {
            if (v.x > 180f) v.x -= 360f; else if (v.x < -180f) v.x += 360f;
            if (v.y > 180f) v.y -= 360f; else if (v.y < -180f) v.y += 360f;
            return v;
        }

        public void Connect()
        {
            if (IsConnected) return;
            if (SimulateEnabled)
            {
                Debug.Log("[Balance] Simulation mode is on - Connect() skipped.");
                return;
            }
            StartSerial();
        }

        void StartSerial()
        {
            if (m_SerialThread != null && m_SerialThread.IsAlive)
            {
                // Restart: ask the old worker to stop, then spin a fresh probe.
                m_SerialStop = true;
                try { m_SerialThread.Join(500); } catch (Exception e) { Debug.LogWarning("[Balance] Serial stop: " + e.Message); }
            }
            m_SerialThread = null;
            m_SerialStop = false;
            m_SerialState = kSerialOff;
            m_SerialConnectedAt = 0f;
            m_SerialNoDataWarned = false;
            m_SerialThread = new Thread(SerialWorker) { IsBackground = true, Name = "BalanceBoardSerial" };
            m_SerialThread.Start();
        }

        // Runs on a background thread. Owns the COM handle for its whole lifetime (probe, then feed).
        void SerialWorker()
        {
            string[] candidates;
            if (!string.IsNullOrEmpty(portName))
            {
                candidates = new[] { portName };
            }
            else
            {
                try { candidates = WinSerial.EnumeratePorts(); }
                catch (Exception e)
                {
                    Debug.LogWarning("[Balance] Could not enumerate COM ports: " + e.Message);
                    return;
                }
                if (candidates == null || candidates.Length == 0)
                {
                    Debug.LogWarning("[Balance] No COM ports found. Is the white adapter plugged in? Hold SPACE to fake the meter.");
                    return;
                }
            }

            try
            {
                foreach (string candidate in candidates)
                {
                    if (m_SerialStop) return;

                    SafeFileHandle handle = WinSerial.TryOpen(candidate, baudRate);
                    if (handle == null || handle.IsInvalid)
                    {
                        if (debugLogging) Debug.Log("[Balance] COM " + candidate + ": " + WinSerial.LastErrorText());
                        if (handle != null) handle.Dispose();
                        continue;
                    }

                    m_SerialHandle = handle;
                    m_SerialPortName = candidate;
                    m_SerialState = kSerialProbing;
                    if (debugLogging) Debug.Log("[Balance] Probing " + candidate + " @ " + baudRate + "...");

                    bool matched = ConsumeSerial(handle, probeOnly: true);
                    if (matched)
                    {
                        m_SerialState = kSerialConnected;
                        ConsumeSerial(handle, probeOnly: false);
                        m_SerialState = kSerialOff;
                        return;
                    }

                    // Not ours (or being stopped) - close and move on.
                    m_SerialHandle = null;
                    m_SerialPortName = null;
                    handle.Dispose();
                }

                m_SerialState = kSerialOff;
                if (!m_SerialStop)
                    Debug.LogWarning("[Balance] No sensor found on any COM port. Check the white adapter is plugged in and the sensor is on; hold SPACE to fake the meter.");
            }
            finally
            {
                m_SerialStop = true;
                if (m_SerialHandle != null)
                {
                    m_SerialHandle.Dispose();
                    m_SerialHandle = null;
                }
                if (m_SerialThread == Thread.CurrentThread) m_SerialThread = null;
            }
        }

        /// <summary>
        /// Reads bytes from the open COM port and parses 0x55 0x61 packets (20 bytes, LE int16:
        /// AngX=Roll @ +14, AngY=Pitch @ +16, AsX=wx @ +8, AsY=wy @ +10). In probe mode it stops as
        /// soon as one valid frame arrives; the port is then kept for real-time feeding.
        /// </summary>
        bool ConsumeSerial(SafeFileHandle handle, bool probeOnly)
        {
            var start = DateTime.UtcNow;
            byte[] chunk = new byte[512];
            byte[] accum = new byte[4096];
            int accumLen = 0;

            while (!m_SerialStop && (!probeOnly || (DateTime.UtcNow - start).TotalSeconds < serialProbeSeconds))
            {
                uint n;
                if (!WinSerial.Read(handle, chunk, out n))
                {
                    if (debugLogging) Debug.LogWarning("[Balance] Serial read: " + WinSerial.LastErrorText());
                    return false;
                }
                if (n == 0)
                {
                    Thread.Sleep(5);
                    continue;
                }
                int count = (int)n;

                if (accumLen + count > accum.Length) accumLen = 0;
                Array.Copy(chunk, 0, accum, accumLen, count);
                accumLen += count;

                int i = 0;
                while (i + 20 <= accumLen)
                {
                    if (accum[i] != 0x55 || accum[i + 1] != 0x61)
                    {
                        i++;
                        continue;
                    }
                    short wx = (short)(accum[i + 8] | (accum[i + 9] << 8));
                    short wy = (short)(accum[i + 10] | (accum[i + 11] << 8));
                    short roll = (short)(accum[i + 14] | (accum[i + 15] << 8));
                    short pitch = (short)(accum[i + 16] | (accum[i + 17] << 8));
                    PushSerialSnapshot(roll * 180f / 32768f, pitch * 180f / 32768f, wx * 2000f / 32768f, wy * 2000f / 32768f);
                    if (probeOnly) return true;
                    i += 20;
                }

                if (i > 0 && accumLen - i > 0)
                {
                    Array.Copy(accum, i, accum, 0, accumLen - i);
                    accumLen -= i;
                }
                else if (i >= accumLen)
                {
                    accumLen = 0;
                }
            }

            return false;
        }

// Called from the serial worker thread - only writes floats into a lock-protected snapshot.
        void PushSerialSnapshot(float rollDeg, float pitchDeg, float wxDegS, float wyDegS)
        {
            lock (m_SnapLock)
            {
                m_SnapAngle = new Vector2(rollDeg, pitchDeg);
                m_SnapRate = new Vector2(wxDegS, wyDegS);
                m_SnapUpdated = true;
            }
        }

        /// <summary>
        /// Minimal Win32 serial layer (CreateFile / DCB / ReadFile / registry port enumeration).
        /// Avoids UnityEngine's managed serial profiles which omit System.IO.Ports entirely.
        /// </summary>
        static class WinSerial
        {
            const uint GENERIC_READ = 0x80000000;
            const uint GENERIC_WRITE = 0x40000000;
            const uint OPEN_EXISTING = 3;
            const uint FILE_ATTRIBUTE_NORMAL = 0x80;

            const int ERROR_SUCCESS = 0;
            const int KEY_QUERY_VALUE = 0x1;
            const int KEY_ENUMERATE_SUB_KEYS = 0x8;
            const int HKEY_LOCAL_MACHINE = unchecked((int)0x80000002);

            [StructLayout(LayoutKind.Explicit, Size = 28)]
            struct DCB
            {
                [FieldOffset(0)] public uint DCBlength;
                [FieldOffset(4)] public uint BaudRate;
                [FieldOffset(8)] public uint Flags; // bitfield: bit0 fBinary ... bit17 fAbortOnError
                [FieldOffset(12)] public ushort wReserved;
                [FieldOffset(14)] public ushort XonLim;
                [FieldOffset(16)] public ushort XoffLim;
                [FieldOffset(18)] public byte ByteSize;
                [FieldOffset(19)] public byte Parity;
                [FieldOffset(20)] public byte StopBits;
                [FieldOffset(21)] public byte XonChar;
                [FieldOffset(22)] public byte XoffChar;
                [FieldOffset(23)] public byte ErrorChar;
                [FieldOffset(24)] public byte EofChar;
                [FieldOffset(25)] public byte EvtChar;
                [FieldOffset(26)] public ushort wReserved1;
            }

            [StructLayout(LayoutKind.Sequential)]
            struct COMMTIMEOUTS
            {
                public uint ReadIntervalTimeout;
                public uint ReadTotalTimeoutMultiplier;
                public uint ReadTotalTimeoutConstant;
                public uint WriteTotalTimeoutMultiplier;
                public uint WriteTotalTimeoutConstant;
            }

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
                IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

            [DllImport("kernel32.dll", SetLastError = true)]
            static extern bool GetCommState(SafeFileHandle hFile, ref DCB lpDCB);

            [DllImport("kernel32.dll", SetLastError = true)]
            static extern bool SetCommState(SafeFileHandle hFile, ref DCB lpDCB);

            [DllImport("kernel32.dll", SetLastError = true)]
            static extern bool SetCommTimeouts(SafeFileHandle hFile, ref COMMTIMEOUTS lpCommTimeouts);

            [DllImport("kernel32.dll", SetLastError = true)]
            static extern bool ReadFile(SafeFileHandle hFile, byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, IntPtr lpOverlapped);

            [DllImport("advapi32.dll", SetLastError = true)]
            static extern int RegOpenKeyEx(int hKey, string lpSubKey, int ulOptions, int samDesired, out int phkResult);

            [DllImport("advapi32.dll", SetLastError = true)]
            static extern int RegQueryInfoKey(int hKey, StringBuilder lpClass, IntPtr lpcClass, IntPtr lpReserved,
                out uint lpcSubKeys, IntPtr lpcMaxSubKeyLen, IntPtr lpcMaxClassLen, out uint lpcValues,
                IntPtr lpcMaxValueNameLen, IntPtr lpcMaxValueLen, IntPtr lpcbSecurityDescriptor, IntPtr lpftLastWriteTime);

            [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            static extern int RegEnumValue(int hKey, uint dwIndex, StringBuilder lpValueName, ref uint lpcchValueName,
                IntPtr lpReserved, IntPtr lpType, byte[] lpData, ref uint lpcbData);

            [DllImport("advapi32.dll")]
            static extern int RegCloseKey(int hKey);

            /// <summary>Returns COM port names found in HKEY_LOCAL_MACHINE\HARDWARE\DEVICEMAP\SERIALCOMM.</summary>
            public static string[] EnumeratePorts()
            {
                var list = new System.Collections.Generic.List<string>();
                int key;
                if (RegOpenKeyEx(HKEY_LOCAL_MACHINE, @"HARDWARE\DEVICEMAP\SERIALCOMM", 0, KEY_QUERY_VALUE | KEY_ENUMERATE_SUB_KEYS, out key) != ERROR_SUCCESS)
                    return list.ToArray();
                try
                {
                    uint subKeyCount, valueCount;
                    if (RegQueryInfoKey(key, null, IntPtr.Zero, IntPtr.Zero, out subKeyCount, IntPtr.Zero, IntPtr.Zero,
                        out valueCount, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) == ERROR_SUCCESS)
                    {
                        byte[] data = new byte[128];
                        for (uint i = 0; i < valueCount; i++)
                        {
                            var name = new StringBuilder(256);
                            uint nameLen = 256;
                            uint dataLen = 128;
                            if (RegEnumValue(key, i, name, ref nameLen, IntPtr.Zero, IntPtr.Zero, data, ref dataLen) == ERROR_SUCCESS)
                            {
                                string com = ReadRegString(data, dataLen);
                                if (!string.IsNullOrEmpty(com)) list.Add(com);
                            }
                        }
                    }
                }
                finally
                {
                    RegCloseKey(key);
                }
                list.Sort();
                return list.ToArray();
            }

            static string ReadRegString(byte[] data, uint len)
            {
                int end = 0;
                while (end < len && data[end] != 0) end++;
                return Encoding.ASCII.GetString(data, 0, end);
            }

            /// <summary>Opens the COM port, configures 8N1 at the requested baud, 50 ms no-data read timeout.</summary>
            public static SafeFileHandle TryOpen(string portName, int baudRate)
            {
                SafeFileHandle handle = CreateFile(@"\\.\" + portName.Trim(), GENERIC_READ | GENERIC_WRITE, 0,
                    IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
                if (handle == null || handle.IsInvalid) return handle;

                DCB dcb = new DCB { DCBlength = 28 };
                if (!GetCommState(handle, ref dcb)) { handle.Dispose(); return new SafeFileHandle(IntPtr.Zero, false); }
                dcb.BaudRate = (uint)baudRate;
                dcb.Flags |= 1; // fBinary must be set
                dcb.ByteSize = 8;
                dcb.Parity = 0; // NOPARITY
                dcb.StopBits = 0; // ONESTOPBIT
                if (!SetCommState(handle, ref dcb)) { handle.Dispose(); return new SafeFileHandle(IntPtr.Zero, false); }

                COMMTIMEOUTS ct = new COMMTIMEOUTS
                {
                    ReadIntervalTimeout = 0,
                    ReadTotalTimeoutMultiplier = 0,
                    ReadTotalTimeoutConstant = 50,
                    WriteTotalTimeoutMultiplier = 0,
                    WriteTotalTimeoutConstant = 100
                };
                if (!SetCommTimeouts(handle, ref ct)) { handle.Dispose(); return new SafeFileHandle(IntPtr.Zero, false); }
                return handle;
            }

            public static bool Read(SafeFileHandle handle, byte[] buffer, out uint read)
            {
                return ReadFile(handle, buffer, (uint)buffer.Length, out read, IntPtr.Zero);
            }

            public static string LastErrorText()
            {
                int code = Marshal.GetLastWin32Error();
                if (code == 0) return "no error";
                try { return new System.ComponentModel.Win32Exception(code).Message; }
                catch { return "Win32 error " + code; }
            }
        }

        /// <summary>Aborts an in-progress scan so a fresh one can start (C key while stuck).</summary>
        void CancelScan()
        {
            if (!IsScanning) return;
            BlueScanner.Instance.StopScan();
            BlueScanner.Instance.OnFindDevice -= OnFindDevice;
            BlueScanner.Instance.OnScanFinished -= OnScanFinished;
            lock (m_ScanLock)
            {
                m_PendingName = null;
                m_PendingId = null;
                m_ScanFinished = false;
            }
            IsScanning = false;
            if (debugLogging) Debug.Log("[Balance] Cancelled previous scan; starting a fresh one.");
        }

        public void Disconnect()
        {
            if (m_Device != null)
            {
                m_Device.OnKeyUpdate -= OnKeyUpdate;
                try { m_Device.CloseDevice(); }
                catch (Exception e) { Debug.LogWarning("[Balance] Disconnect: " + e.Message); }
                DevicesManager.Instance.RemoveDevice(m_Device.deviceId);
                m_Device = null;
            }
            BlueScanner.Instance.OnFindDevice -= OnFindDevice;
            BlueScanner.Instance.OnScanFinished -= OnScanFinished;
            m_Calibrating = false;
            IsCalibrated = false;
            m_CalibOffset = Vector2.zero;
            IsScanning = false;

            m_SerialStop = true;
            Thread t = m_SerialThread;
            if (t != null)
            {
                try { t.Join(500); } catch (Exception e) { Debug.LogWarning("[Balance] Serial thread stop: " + e.Message); }
            }
            m_SerialThread = null;
            m_SerialState = kSerialOff;
            m_SerialConnectedAt = 0f;
            m_SerialNoDataWarned = false;
        }

        /// <summary>
        /// Records the current board pose as the flat reference. In simulation mode this applies
        /// instantly; with live hardware it averages the next calibrateSamples angle updates.
        /// </summary>
        public void CalibrateFlat()
        {
            m_Calibrating = true;
            if (SimulateEnabled)
            {
                SetCalibrated(SimRawTilt);
                return;
            }
            m_CalibSum = Vector2.zero;
            m_CalibCount = 1;
            Debug.Log($"[Balance] Calibrating flat over the next {calibrateSamples} samples...");
        }

        // Called from the BLE receive thread.
        void OnFindDevice(string name, string id)
        {
            lock (m_ScanLock)
            {
                // Keep a pending device that already matches; otherwise always adopt the newest
                // device seen so a matching sensor can't be locked out by an earlier non-match.
                if (m_PendingId != null && MatchesFilter(m_PendingName)) return;
                m_PendingName = name;
                m_PendingId = id;
            }
            if (debugLogging)
                Debug.Log($"[Balance] Seen device '{name}' ({id})" + (MatchesFilter(name) ? " -> matches filter" : " -> does not match '" + deviceNameFilter + "'"));
        }

        bool MatchesFilter(string name)
        {
            return !string.IsNullOrEmpty(name) &&
                   (string.IsNullOrEmpty(deviceNameFilter) ||
                    name.IndexOf(deviceNameFilter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        void OnScanFinished(string _, string __)
        {
            lock (m_ScanLock) m_ScanFinished = true;
        }

        void ConnectFromPendingScan()
        {
            string name = null;
            string id = null;
            bool scanFinished = false;
            lock (m_ScanLock)
            {
                if (MatchesFilter(m_PendingName))
                {
                    name = m_PendingName;
                    id = m_PendingId;
                    m_PendingName = null;
                    m_PendingId = null;
                }
                scanFinished = m_ScanFinished;
            }

            if (scanFinished && IsScanning)
            {
                BlueScanner.Instance.OnFindDevice -= OnFindDevice;
                BlueScanner.Instance.OnScanFinished -= OnScanFinished;
                IsScanning = false;
                if (debugLogging)
                    Debug.Log("[Balance] Scan finished with no device matching '" + deviceNameFilter + "'. Press C to rescan.");
            }

            if (id == null) return;

            BlueScanner.Instance.StopScan();
            BlueScanner.Instance.OnFindDevice -= OnFindDevice;
            BlueScanner.Instance.OnScanFinished -= OnScanFinished;
            IsScanning = false;

            var device = new DeviceModel(name, id);
            DevicesManager.Instance.AddDevice(device);
            m_Device = device;
            m_Device.OnKeyUpdate += OnKeyUpdate;
            m_Device.OpenDevice();
            m_DeviceOpenedAt = Time.time;
            m_NoDataWarned = false;
            Debug.Log($"[Balance] Connected to {name} ({id}). Press B to calibrate flat.");
        }

        // Called from the BLE receive thread - only writes doubles into a lock-protected snapshot.
        void OnKeyUpdate(DeviceModel device)
        {
            lock (m_SnapLock)
            {
                m_SnapAngle = new Vector2((float)device.GetDeviceData("AngX"), (float)device.GetDeviceData("AngY"));
                m_SnapRate = new Vector2((float)device.GetDeviceData("AsX"), (float)device.GetDeviceData("AsY"));
                m_SnapBattery = (float)device.GetDeviceData("Battery");
                m_SnapUpdated = true;
            }
        }

        void HandleKeyboard()
        {
            if (Keyboard.current == null) return;

            if (Keyboard.current.cKey.wasPressedThisFrame)
            {
                if (IsConnected)
                {
                    // silent - already connected
                }
                else if (IsScanning)
                {
                    CancelScan();
                    Connect();
                }
                else
                {
                    Connect();
                }
            }
            if (Keyboard.current.bKey.wasPressedThisFrame)
            {
                if (IsConnected)
                    CalibrateFlat();
                else
                    Debug.Log("[Balance] No sensor connected - hold SPACE to drive the balance meter manually.");
            }

            if (!SimulateEnabled) return;

            Vector2 target = SimRawTilt;
            if (Keyboard.current.leftArrowKey.isPressed) target.x -= 40f * Time.deltaTime;
            if (Keyboard.current.rightArrowKey.isPressed) target.x += 40f * Time.deltaTime;
            if (Keyboard.current.upArrowKey.isPressed) target.y += 40f * Time.deltaTime;
            if (Keyboard.current.downArrowKey.isPressed) target.y -= 40f * Time.deltaTime;
            SimRawTilt = target;
            SimRate = Keyboard.current.spaceKey.isPressed
                ? Vector2.Lerp(SimRate, new Vector2(0f, 70f), Time.deltaTime * 4f)
                : Vector2.Lerp(SimRate, Vector2.zero, Time.deltaTime * 4f);
        }
    }
}