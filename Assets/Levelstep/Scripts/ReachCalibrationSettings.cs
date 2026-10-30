using UnityEngine;

namespace Levelstep
{
    /// <summary>Read-only, Inspector-authored distances for a future consumer. No measurement or grappling behaviour.</summary>
    public interface IReachCalibrationSettings
    {
        float ComfortableHorizontalReachDistance { get; }
        float ComfortableVerticalReachDistance { get; }
        float MaximumGrapplingDistance { get; }
    }

    [DisallowMultipleComponent]
    public class ReachCalibrationSettings : MonoBehaviour, IReachCalibrationSettings
    {
        [Header("Predetermined prototype distances (metres)")]
        [Tooltip("Authored comfortable horizontal reach distance, not sampled from a controller.")]
        [SerializeField, Min(0f)] float comfortableHorizontalReachDistance = 0.45f;
        [Tooltip("Authored comfortable vertical reach distance; this is a distance, not a world-space height.")]
        [SerializeField, Min(0f)] float comfortableVerticalReachDistance = 0.6f;
        [Tooltip("Configured limit for a future grappling system. No grappling is implemented by this component.")]
        [SerializeField, Min(0f)] float maximumGrapplingDistance = 3f;

        public float ComfortableHorizontalReachDistance => Mathf.Max(0f, comfortableHorizontalReachDistance);
        public float ComfortableVerticalReachDistance => Mathf.Max(0f, comfortableVerticalReachDistance);
        public float MaximumGrapplingDistance => Mathf.Max(0f, maximumGrapplingDistance);
    }
}
