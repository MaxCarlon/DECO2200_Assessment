using Assets.Device;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Assets.Bluetooth
{
    /*
     * Bluetooth connector - connects to a device, discovers the real service/characteristic UUIDs it
     * exposes, subscribes to its notify characteristic and pumps received bytes to listeners.
     *
     * Discovery is needed because WitMotion sensors may expose either the classic WitMotion custom
     * service (0xFFE5/0xFFE4/0xFFE9) or the generic Nordic NUS "UART" service (6E400001) - e.g. modules
     * advertising the default "nRF52 USB CDC BLE Demo" name. We try every usable service and pick the
     * best notify/write pair automatically.
     */
    public class BlueConnector
    {
        private static BlueConnector _instance;

        // 经典 WitMotion 服务 Classic WitMotion service
        public static readonly string UUID_SERVICE = "0000ffe5-0000-1000-8000-00805f9a34fb";
        public static readonly string UUID_READ = "0000ffe4-0000-1000-8000-00805f9a34fb";
        public static readonly string UUID_WRITE = "0000ffe9-0000-1000-8000-00805f9a34fb";

        // Nordic NUS "UART over BLE" service
        public static readonly string UUID_NUS_SERVICE = "6e400001-b5a3-f393-e0a9-e50e24dcca9e";
        public static readonly string UUID_NUS_TX = "6e400003-b5a3-f393-e0a9-e50e24dcca9e";
        public static readonly string UUID_NUS_RX = "6e400002-b5a3-f393-e0a9-e50e24dcca9e";

        // 接收数据线程 Receive data thread
        private Thread receiveTh;

        public bool isConnect = false;

        // UUIDs of the service/characteristics actually used for the current device.
        public string CurrentServiceUuid = UUID_SERVICE;
        public string CurrentReadUuid = UUID_READ;
        public string CurrentWriteUuid = UUID_WRITE;

        // 收到数据事件 Received data event
        public delegate void ReceiveEventHandler(string deviceId, byte[] data);
        public event ReceiveEventHandler OnReceive;

        private BlueConnector() { }

        public static BlueConnector Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new BlueConnector();
                }
                return _instance;
            }
        }

        /// <summary>
        /// 开始连接 Start connecting
        /// </summary>
        public void Connect(string deviceId)
        {
            try
            {
                if (!DiscoverAndSubscribe(deviceId))
                {
                    Debug.LogWarning("[Balance] BLE connect ok, but no usable notify service found on '" + deviceId + "'.");
                    return;
                }

                if (isConnect)
                {
                    return;
                }
                isConnect = true;
                receiveTh = new Thread(ReceiveData);
                receiveTh.IsBackground = true;
                receiveTh.Start();
            }
            catch (Exception ex)
            {
                Debug.LogError("[Balance] Connect failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Enumerates the device's services + characteristics and subscribes to the best notify
        /// characteristic found (classic WitMotion service preferred, then Nordic NUS, then any).
        /// </summary>
        bool DiscoverAndSubscribe(string deviceId)
        {
            List<string> services = PollServices(deviceId);
            if (services.Count == 0)
            {
                Debug.LogWarning("[Balance] No services found on '" + deviceId + "'.");
                return false;
            }

            string service = PickService(services);
            List<string> chars = PollCharacteristics(deviceId, service);
            if (chars.Count == 0)
            {
                Debug.LogWarning("[Balance] No characteristics found on service '" + service + "'.");
                return false;
            }

            string notify = PickNotifyChar(chars, service);
            string write = PickWriteChar(chars, service, notify);

            if (!BleApi.SubscribeCharacteristic(deviceId, service, notify, false))
            {
                Debug.LogWarning("[Balance] SubscribeCharacteristic failed on '" + service + "' / '" + notify + "'.");
                return false;
            }

            CurrentServiceUuid = service;
            CurrentReadUuid = notify;
            CurrentWriteUuid = write;
            Debug.Log("[Balance] Using BLE service " + service + " | notify " + notify + " | write " + write);
            return true;
        }

        List<string> PollServices(string deviceId)
        {
            List<string> result = new List<string>();
            BleApi.ScanServices(deviceId);
            int tries = 0;
            while (tries++ < 600)
            {
                BleApi.Service svc;
                BleApi.ScanStatus status = BleApi.PollService(out svc, false);
                if (status == BleApi.ScanStatus.AVAILABLE)
                {
                    if (!string.IsNullOrEmpty(svc.uuid)) result.Add(svc.uuid);
                }
                else if (status == BleApi.ScanStatus.FINISHED)
                {
                    break;
                }
                Thread.Sleep(5);
            }
            return result;
        }

        List<string> PollCharacteristics(string deviceId, string serviceUuid)
        {
            List<string> result = new List<string>();
            BleApi.ScanCharacteristics(deviceId, serviceUuid);
            int tries = 0;
            while (tries++ < 600)
            {
                BleApi.Characteristic ch;
                BleApi.ScanStatus status = BleApi.PollCharacteristic(out ch, false);
                if (status == BleApi.ScanStatus.AVAILABLE)
                {
                    if (!string.IsNullOrEmpty(ch.uuid)) result.Add(ch.uuid);
                }
                else if (status == BleApi.ScanStatus.FINISHED)
                {
                    break;
                }
                Thread.Sleep(5);
            }
            return result;
        }

        string PickService(List<string> services)
        {
            foreach (string s in services)
            {
                if (ContainsIc(s, "ffe5")) return s;
            }
            foreach (string s in services)
            {
                if (ContainsIc(s, "6e400001")) return s;
            }
            return services[0];
        }

        string PickNotifyChar(List<string> chars, string serviceUuid)
        {
            if (ContainsIc(serviceUuid, "ffe5"))
            {
                foreach (string c in chars)
                {
                    if (ContainsIc(c, "ffe4")) return c;
                }
            }
            else if (ContainsIc(serviceUuid, "6e400001"))
            {
                foreach (string c in chars)
                {
                    if (ContainsIc(c, "6e400003")) return c;
                }
                foreach (string c in chars)
                {
                    if (ContainsIc(c, "6e400002")) return c;
                }
            }
            return chars[0];
        }

        string PickWriteChar(List<string> chars, string serviceUuid, string fallback)
        {
            if (ContainsIc(serviceUuid, "ffe5"))
            {
                foreach (string c in chars)
                {
                    if (ContainsIc(c, "ffe9")) return c;
                }
            }
            else if (ContainsIc(serviceUuid, "6e400001"))
            {
                foreach (string c in chars)
                {
                    if (ContainsIc(c, "6e400002")) return c;
                }
            }
            return fallback;
        }

        static bool ContainsIc(string haystack, string needle)
        {
            return haystack != null && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 接收数据线程 Receive data thread
        /// </summary>
        private void ReceiveData()
        {
            BleApi.BLEData res = new BleApi.BLEData();
            while (true)
            {
                while (isConnect && BleApi.PollData(out res, false))
                {
                    OnReceive?.Invoke(res.deviceId, res.buf);
                }
                Thread.Sleep(1);
            }
        }

        /// <summary>
        /// 关闭连接 Close Connection
        /// </summary>
        public void Disconnect()
        {
            if (DevicesManager.Instance.isHaveOpenDevice())
            {
                return;
            }
            try
            {
                isConnect = false;
                Thread.Sleep(200);
                receiveTh.Abort();
                receiveTh = null;
            }
            catch (Exception)
            {
                // 捕捉异常但不处理
            }
        }
    }
}