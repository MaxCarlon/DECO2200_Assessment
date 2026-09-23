using UnityEngine;

namespace Levelstep
{
    /// <summary>
    /// Holds one board's balance-meter settings so the live BalanceMeterController can be swapped
    /// between two boards ("chain" and "loop") with K/L during a playtest. Adding a concrete preset
    /// script to the 'Balance Board' object shows it in the Inspector as a full settings block you
    /// edit per board.
    /// </summary>
    public abstract class BalanceMeterPreset : MonoBehaviour
    {
        [Tooltip("The live balance meter controller this preset drives. Leave unassigned to auto-resolve BalanceMeterController.Instance.")]
        public BalanceMeterController controller;

        [Tooltip("At or below this calibrated tilt (degrees) the board counts as stable (no meter gain).")]
        public float stableTiltDeg = 4f;

        [Tooltip("At or above this calibrated tilt instability is full (meter builds fastest).")]
        public float unstableTiltDeg = 8f;

        [Tooltip("Tilt at which balance is completely lost - you fall immediately.")]
        public float hardFailTiltDeg = 16f;

        [Tooltip("At or below this angular rate (°/s) the board counts as stable.")]
        public float stableRateDegS = 40f;

        [Tooltip("At or above this angular rate instability is full.")]
        public float unstableRateDegS = 120f;

        [Tooltip("Angular rate spike that counts as completely losing balance (instant fall).")]
        public float hardFailRateDegS = 250f;

        [Tooltip("How fast the meter fills (0->1) while instability is full.")]
        public float buildPerSec = 0.5f;

        [Tooltip("How fast the meter drains back to zero while fully stable.")]
        public float recoverPerSec = 0.5f;

        [Tooltip("Response smoothing of instability (higher reacts quicker).")]
        public float instabilitySmoothing = 4f;

        [Tooltip("Minimum seconds between falls so a hard spike can't trigger back-to-back falls.")]
        public float minSecondsBetweenFalls = 1f;

        /// <summary>
        /// Copies this board's settings onto the live controller. Does not touch the meter's current
        /// value, so swapping boards mid-run keeps the in-progress meter. Config overrides are
        /// disabled so the preset values always win.
        /// </summary>
        public void Apply()
        {
            var c = controller != null ? controller : BalanceMeterController.Instance;
            if (c == null)
            {
                Debug.LogWarning("[Balance] " + GetType().Name + " can't apply: no BalanceMeterController found.", this);
                return;
            }
            c.readThresholdsFromConfig = false;
            c.stableTiltDeg = stableTiltDeg;
            c.unstableTiltDeg = unstableTiltDeg;
            c.hardFailTiltDeg = hardFailTiltDeg;
            c.stableRateDegS = stableRateDegS;
            c.unstableRateDegS = unstableRateDegS;
            c.hardFailRateDegS = hardFailRateDegS;
            c.buildPerSec = buildPerSec;
            c.recoverPerSec = recoverPerSec;
            c.instabilitySmoothing = instabilitySmoothing;
            c.minSecondsBetweenFalls = minSecondsBetweenFalls;
        }
    }
}