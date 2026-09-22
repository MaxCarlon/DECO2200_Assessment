using UnityEngine;

namespace Levelstep
{
    public class LevelstepFrameRate : MonoBehaviour
    {
        [Tooltip("Target frames per second for laptop streaming (e.g. 90 for a 90 Hz headset).")]
        public int targetFrameRate = 90;

        [Tooltip("Disable vsync so the frame cap paces the renderer instead of the laptop monitor.")]
        public bool disableVsync = true;

        void Awake()
        {
            if (targetFrameRate > 0)
                Application.targetFrameRate = targetFrameRate;
            if (disableVsync)
                QualitySettings.vSyncCount = 0;
        }
    }
}