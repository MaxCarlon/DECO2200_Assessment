using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Levelstep
{
    /// <summary>
    /// Turns the balance board's calibrated tilt + angular rate into a gameplay "are you falling" meter.
    /// The meter fills while you are unstable and drains back to zero while you are stable. When the
    /// meter tops out (or the board completely loses balance) it zeroes both hands' stamina - the
    /// existing ClimbStaminaController exhaustion path then releases the grips, blocks re-grabbing and
    /// regenerates like normal, so the player physically falls off the wall.
    ///
    /// Falls once per full bar: after a fall the meter stays full and no further falls fire, and both
    /// hands stay exhausted with no stamina, until the bar actually starts draining (the board must
    /// level out, even briefly). Only once the bar recovers does stamina regeneration resume, so the
    /// player can keep climbing instead of being re-fallen every second.
    /// 
    /// All thresholds live on this component so, during a playtest, you can select the "Balance Board"
    /// object in the Inspector and edit them live while playing.
    /// 
    /// Keyboard (play mode):
    ///   [ / ] nudge "stable" tilt        - / = nudge "unstable" tilt      T toggle debug overlay
    /// </summary>
    public class BalanceMeterController : MonoBehaviour
    {
        [Tooltip("Optional config asset. If unassigned, the serialized values below are used.")]
        public LevelstepClimbingConfig config;

        [Tooltip("Copy the balance threshold/meter values from the ClimbingConfig asset at Start. Leave OFF to use (and keep) the values set on this component, including in builds.")]
        public bool readThresholdsFromConfig = false;

        [Tooltip("Sensor bridge. Leave unassigned to auto-resolve BalanceBoardBridge.Instance.")]
        public BalanceBoardBridge bridge;

        [Tooltip("Hand stamina controller that releases grips on a fall. Leave unassigned to auto-resolve.")]
        public ClimbStaminaController stamina;

        [Header("Stability thresholds - select this object and tweak LIVE while playtesting")]
        [Tooltip("At or below this calibrated tilt (degrees) the board counts as stable (no meter gain).")]
        public float stableTiltDeg = 4f;

        [Tooltip("At or above this calibrated tilt instability is full (meter builds fastest).")]
        public float unstableTiltDeg = 14f;

        [Tooltip("Tilt at which balance is completely lost - you fall immediately.")]
        public float hardFailTiltDeg = 30f;

        [Tooltip("At or below this angular rate (°/s) the board counts as stable.")]
        public float stableRateDegS = 40f;

        [Tooltip("At or above this angular rate instability is full.")]
        public float unstableRateDegS = 120f;

        [Tooltip("Angular rate spike that counts as completely losing balance (instant fall).")]
        public float hardFailRateDegS = 250f;

        [Header("Meter")]
        [Tooltip("How fast the meter fills (0->1) while instability is full.")]
        public float buildPerSec = 0.5f;

        [Tooltip("How fast the meter drains back to zero while fully stable.")]
        public float recoverPerSec = 0.35f;

        [Tooltip("Response smoothing of instability (higher reacts quicker).")]
        public float instabilitySmoothing = 8f;

        [Tooltip("Minimum seconds between falls so a hard spike can't trigger back-to-back falls.")]
        public float minSecondsBetweenFalls = 1f;

        [Header("Failsafe")]
        [Tooltip("Hold SPACE to force the meter to fill (acts as an extremely unstable board). Use if the sensor dies on the day.")]
        public bool spaceFailsafe = true;

        [Header("Debug")]
        [Tooltip("Show a live readout + thresholds overlay (toggle in play with T).")]
        public bool debugOverlay = false;

        public static BalanceMeterController Instance { get; private set; }

        /// <summary>Current balance meter, 0 (stable) to 1 (falling).</summary>
        public float Meter { get; private set; }

        /// <summary>Current instability 0..1 derived from tilt + rate.</summary>
        public float Instability { get; private set; }

        public float TiltMagnitude { get; private set; }
        public float RateMagnitude { get; private set; }
        public int FellCount { get; private set; }
        public float TimeSinceLastFall { get; private set; }

        public event Action FellOff;
        public event Action<float> MeterChanged;

        BalanceBoardBridge m_Bridge;
        ClimbStaminaController m_Stamina;
        bool m_SpaceHeld;
        bool m_WaitingToRefill;

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            ApplyConfig();
        }

        void ApplyConfig()
        {
            if (!readThresholdsFromConfig || config == null || config.balanceStableTiltDeg <= 0f) return;
            stableTiltDeg = config.balanceStableTiltDeg;
            unstableTiltDeg = config.balanceUnstableTiltDeg;
            hardFailTiltDeg = config.balanceHardFailTiltDeg;
            stableRateDegS = config.balanceStableRateDegS;
            unstableRateDegS = config.balanceUnstableRateDegS;
            hardFailRateDegS = config.balanceHardFailRateDegS;
            buildPerSec = config.balanceMeterBuildPerSec;
            recoverPerSec = config.balanceMeterRecoverPerSec;
            minSecondsBetweenFalls = config.balanceMinSecondsBetweenFalls;
        }

        void Update()
        {
            HandleKeyboard();
            if (Time.deltaTime > 0f)
                Tick(Time.deltaTime);
        }

        /// <summary>
        /// Advances the balance simulation by one fixed step. Public so headless sanity checks and
        /// tests can drive the controller deterministically without entering play mode.
        /// </summary>
        public void Tick(float dt)
        {
            if (m_Bridge == null) m_Bridge = bridge != null ? bridge : BalanceBoardBridge.Instance;
            if (m_Stamina == null) m_Stamina = stamina != null ? stamina : ClimbStaminaController.Instance;
            if (m_Bridge == null) return;

            m_Bridge.Refresh();
            TiltMagnitude = m_Bridge.TiltDeg.magnitude;
            RateMagnitude = m_Bridge.AngularRateDegS.magnitude;

            if (m_SpaceHeld && spaceFailsafe)
            {
                TiltMagnitude = Mathf.Max(TiltMagnitude, unstableTiltDeg);
                RateMagnitude = Mathf.Max(RateMagnitude, unstableRateDegS);
            }

            float target = ComputeInstability(TiltMagnitude, RateMagnitude);
            Instability = Mathf.Lerp(Instability, target, Mathf.Clamp01(instabilitySmoothing * dt));

            Meter += Instability * buildPerSec * dt;
            Meter -= (1f - Instability) * recoverPerSec * dt;
            Meter = Mathf.Clamp01(Meter);
            MeterChanged?.Invoke(Meter);

            if (m_WaitingToRefill && Meter < 1f)
            {
                m_WaitingToRefill = false;
                m_Stamina?.SetBalanceLock(false);
            }

            TimeSinceLastFall += dt;

            bool hardFail = TiltMagnitude >= hardFailTiltDeg || RateMagnitude >= hardFailRateDegS;
            bool canFall = hardFail || (Meter >= 1f && !m_WaitingToRefill);
            if (canFall && TimeSinceLastFall >= minSecondsBetweenFalls)
                Fall();
        }

        float ComputeInstability(float tilt, float rate)
        {
            float score = 0f;
            if (tilt > stableTiltDeg)
                score = Mathf.Max(score, Mathf.Clamp01((tilt - stableTiltDeg) / Range01(unstableTiltDeg, stableTiltDeg)));
            if (rate > stableRateDegS)
                score = Mathf.Max(score, Mathf.Clamp01((rate - stableRateDegS) / Range01(unstableRateDegS, stableRateDegS)));
            return score;
        }

        static float Range01(float high, float low)
        {
            float d = high - low;
            return d > 0.0001f ? d : 1f;
        }

        void Fall()
        {
            TimeSinceLastFall = 0f;
            Meter = 1f;
            m_WaitingToRefill = true;
            FellCount++;
            if (m_Stamina != null)
            {
                m_Stamina.FallFromBalance();
                m_Stamina.SetBalanceLock(true);
            }
            FellOff?.Invoke();
            Debug.Log($"[Balance] Fell off the wall! Falls: {FellCount} (tilt {TiltMagnitude:F1}°, rate {RateMagnitude:F0}°/s)");
        }

        void HandleKeyboard()
        {
            if (Keyboard.current == null) return;

            m_SpaceHeld = Keyboard.current.spaceKey.isPressed;

            if (Keyboard.current.leftBracketKey.wasPressedThisFrame)
            {
                stableTiltDeg = Mathf.Max(0f, stableTiltDeg - 0.5f);
                Debug.Log($"[Balance] Stable tilt threshold {stableTiltDeg:F1}°  (\u005b \u005d to set)");
            }
            if (Keyboard.current.rightBracketKey.wasPressedThisFrame)
            {
                stableTiltDeg += 0.5f;
                Debug.Log($"[Balance] Stable tilt threshold {stableTiltDeg:F1}°  (\u005b \u005d to set)");
            }
            if (Keyboard.current.minusKey.wasPressedThisFrame)
            {
                unstableTiltDeg = Mathf.Max(stableTiltDeg + 0.5f, unstableTiltDeg - 0.5f);
                Debug.Log($"[Balance] Unstable tilt threshold {unstableTiltDeg:F1}°  (- = to set)");
            }
            if (Keyboard.current.equalsKey.wasPressedThisFrame)
            {
                unstableTiltDeg = Mathf.Max(stableTiltDeg + 0.5f, unstableTiltDeg + 0.5f);
                Debug.Log($"[Balance] Unstable tilt threshold {unstableTiltDeg:F1}°  (- = to set)");
            }
            if (Keyboard.current.tKey.wasPressedThisFrame)
            {
                debugOverlay = !debugOverlay;
                Debug.Log($"[Balance] Debug overlay {(debugOverlay ? "on" : "off")} (press T to toggle)");
            }
        }

        /// <summary>Multi-line readout for the debug overlay: live values + the three threshold groups.</summary>
        public string DebugLine =>
            $"Stable  <{stableTiltDeg:F1}\u00b0 / <{stableRateDegS:F0}\u00b0/s\n" +
            $"Unstable >{unstableTiltDeg:F1}\u00b0 / >{unstableRateDegS:F0}\u00b0/s\n" +
            $"Fail    >{hardFailTiltDeg:F1}\u00b0 / >{hardFailRateDegS:F0}\u00b0/s\n\n" +
            $"Tilt {TiltMagnitude:F1}\u00b0   Rate {RateMagnitude:F0}\u00b0/s\n" +
            $"Instability {Instability:F2}   Meter {Meter:F2}\n" +
            $"Falls {FellCount}\n\n" +
            $"\u005b/\u005d stable\u00b0   -/= unstable\u00b0   T overlay   B recalibrate\n" +
            $"Hold SPACE to fill meter (failsafe)";
    }
}