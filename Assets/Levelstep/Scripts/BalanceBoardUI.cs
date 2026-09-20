using UnityEngine;
using UnityEngine.UI;

namespace Levelstep
{
    /// <summary>
    /// Headset-view balance HUD and route timer. The HUD panel is authored in the scene as a
    /// world-space canvas (so it can be seen and tuned in the Editor), and this component keeps it
    /// locked in front of the headset camera while driving the bar fill and timer text.
    /// </summary>
    public class BalanceBoardUI : MonoBehaviour
    {
        const float ResultShowSeconds = 4f;

        [Tooltip("Optional config asset. If unassigned, the serialized values below are used.")]
        public LevelstepClimbingConfig config;

        [Tooltip("Show the balance meter at all.")]
        public bool hudEnabled = true;

        [Header("HUD references (authored in the scene)")]
        [Tooltip("Root of the HUD panel. Locked in front of the camera at runtime.")]
        public Transform hudRoot;

        [Tooltip("The HUD canvas.")]
        public Canvas canvas;

        [Tooltip("The empty bar the fill sits in. Its width sizes the fill.")]
        public Image backImage;

        [Tooltip("The coloured balance fill; its width is driven by the meter.")]
        public Image fillImage;

        [Tooltip("Text shown near the top while a route runs or after it ends.")]
        public Text timerText;

        [Tooltip("Optional debug overlay text.")]
        public Text debugText;

        [Header("Camera lock")]
        [Tooltip("Keep the HUD locked in front of the headset.")]
        public bool lockToCamera = true;

        [Tooltip("Distance the panel floats in front of the eyes (metres).")]
        public float distance = 0.7f;

        [Tooltip("Vertical offset from the eye line (metres, negative = lower in the view).")]
        public float verticalOffset = -0.1f;

        [Header("Colours")]
        public Color barBackColor = new Color(0f, 0f, 0f, 0.55f);
        public Color barFillColor = new Color(0.95f, 0.2f, 0.2f, 0.9f);

        [Header("Route timer")]
        [Tooltip("Show the route timer (starts on grabbing Start, stops on Finish or a fall).")]
        public bool routeTimerEnabled = true;
        public Color routeTimerColor = Color.white;
        [Tooltip("Text colour for a successfully finished run.")]
        public Color routeSuccessColor = new Color(0.3f, 1f, 0.5f);
        [Tooltip("Text colour for an aborted (fallen) run.")]
        public Color routeFailColor = new Color(1f, 0.35f, 0.35f);

        BalanceMeterController m_Meter;
        Transform m_Camera;
        float m_BarWidth;
        float m_BarHeight;
        bool m_Show;
        bool m_CameraLogged;
        bool m_NoCameraLogged;

        void Start()
        {
            m_Meter = BalanceMeterController.Instance;
            m_Show = hudEnabled && (config == null || config.balanceHudEnabled);

            ResolveReferences();

            if (!m_Show)
            {
                if (hudRoot != null) hudRoot.gameObject.SetActive(false);
                return;
            }

            if (config != null)
            {
                barBackColor = config.balanceBarBackColor;
                barFillColor = config.balanceBarFillColor;
                routeTimerColor = config.routeTimerColor;
            }

            if (backImage != null)
            {
                m_BarWidth = backImage.rectTransform.rect.width;
                m_BarHeight = backImage.rectTransform.rect.height;
                backImage.color = barBackColor;
            }
            if (fillImage != null)
            {
                if (m_BarHeight <= 0f) m_BarHeight = fillImage.rectTransform.rect.height;
                fillImage.color = barFillColor;
            }
            if (timerText != null)
            {
                timerText.color = routeTimerColor;
                timerText.gameObject.SetActive(false);
            }
            if (debugText != null)
                debugText.gameObject.SetActive(false);

            if (hudRoot != null) hudRoot.gameObject.SetActive(true);
        }

        void LateUpdate()
        {
            if (!m_Show) return;
            if (m_Meter == null) m_Meter = BalanceMeterController.Instance;

            if (lockToCamera && hudRoot != null)
            {
                if (m_Camera == null) UpdateCamera();
                if (m_Camera != null)
                {
                    hudRoot.position = m_Camera.position + m_Camera.forward * distance + m_Camera.up * verticalOffset;
                    hudRoot.rotation = m_Camera.rotation;
                }
            }

            if (fillImage != null && m_Meter != null)
                fillImage.rectTransform.sizeDelta = new Vector2(m_BarWidth * Mathf.Clamp01(m_Meter.Meter), m_BarHeight);

            if (debugText != null)
            {
                bool active = m_Meter != null && m_Meter.debugOverlay;
                if (active && m_Meter != null && debugText.text != m_Meter.DebugLine)
                    debugText.text = m_Meter.DebugLine;
                if (debugText.gameObject.activeSelf != active) debugText.gameObject.SetActive(active);
            }

            UpdateTimerText();
        }

        void ResolveReferences()
        {
            if (hudRoot == null)
            {
                var found = transform.Find("BalanceBoardHUD");
                if (found != null) hudRoot = found;
            }
            if (hudRoot == null && canvas != null) hudRoot = canvas.transform;
            if (hudRoot != null)
            {
                if (canvas == null) canvas = hudRoot.GetComponent<Canvas>();
                if (backImage == null) backImage = FindImage(hudRoot, "Back");
                if (fillImage == null) fillImage = FindImage(hudRoot, "Fill");
                if (timerText == null) timerText = FindText(hudRoot, "Timer");
                if (debugText == null) debugText = FindText(hudRoot, "Debug");
            }
        }

        static Image FindImage(Transform root, string name)
        {
            foreach (var image in root.GetComponentsInChildren<Image>(true))
                if (image.gameObject.name == name) return image;
            return null;
        }

        static Text FindText(Transform root, string name)
        {
            foreach (var text in root.GetComponentsInChildren<Text>(true))
                if (text.gameObject.name == name) return text;
            return null;
        }

        void UpdateCamera()
        {
            m_Camera = FindCamera();
            if (m_Camera != null)
            {
                if (canvas != null)
                {
                    var cam = m_Camera.GetComponent<Camera>();
                    if (cam != null) canvas.worldCamera = cam;
                }
                if (!m_CameraLogged)
                {
                    m_CameraLogged = true;
                    Debug.Log("[BalanceBoardUI] HUD locked to camera '" + m_Camera.name + "' at " +
                              m_Camera.position.ToString("F2") + " (distance " + distance + ").");
                }
            }
            else if (!m_NoCameraLogged && Time.time > 1f)
            {
                m_NoCameraLogged = true;
                Debug.LogWarning("[BalanceBoardUI] No Camera found within 1s - the headset HUD stays hidden until one appears.");
            }
        }

        void UpdateTimerText()
        {
            if (timerText == null) return;
            bool enabled = config != null ? config.showRouteTimer : routeTimerEnabled;
            if (!enabled)
            {
                timerText.text = "";
                timerText.gameObject.SetActive(false);
                return;
            }

            var timer = RouteTimer.Instance;
            string text;
            Color color = routeTimerColor;

            if (timer == null || timer.CurrentState == RouteTimer.State.Idle)
            {
                text = "";
            }
            else if (timer.CurrentState == RouteTimer.State.Running)
            {
                text = FormatTime(timer.Elapsed);
            }
            else if (Time.time - timer.LastEndTime < ResultShowSeconds)
            {
                text = (timer.LastSuccess ? "FINISH  " : "FELL  ") + FormatTime(timer.LastResult);
                color = timer.LastSuccess ? routeSuccessColor : routeFailColor;
            }
            else
            {
                text = "";
            }

            timerText.text = text;
            timerText.color = color;
            timerText.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }

        static string FormatTime(float seconds)
        {
            int ms = Mathf.FloorToInt(seconds * 10f) % 10;
            int total = Mathf.FloorToInt(seconds);
            return string.Format("{0}:{1:00}.{2}", total / 60, total % 60, ms);
        }

        static Transform FindCamera()
        {
            if (Camera.main != null) return Camera.main.transform;
            var cams = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include);
            if (cams.Length == 0) return null;
            foreach (var cam in cams)
                if (cam.isActiveAndEnabled)
                    return cam.transform;
            return cams[0].transform;
        }
    }
}
