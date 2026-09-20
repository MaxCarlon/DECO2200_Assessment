using UnityEngine;
using UnityEngine.UI;

namespace Levelstep
{
    [RequireComponent(typeof(Button))]
    [DisallowMultipleComponent]
    public class DifficultyMenuButton : MonoBehaviour
    {
        [Tooltip("Which difficulty preset this UI button selects.")]
        public int index = 1;

        Image m_Background;
        Color m_BaseColor;
        Color m_Bright;
        Color m_Dim;

        void OnEnable()
        {
            DifficultyRouteController.SelectionChanged += OnSelectionChanged;
        }

        void OnDisable()
        {
            DifficultyRouteController.SelectionChanged -= OnSelectionChanged;
        }

        void Start()
        {
            m_Background = GetComponent<Image>();
            if (m_Background != null)
                m_BaseColor = m_Background.color;

            m_Bright = Color.Lerp(m_BaseColor, Color.white, 0.35f);
            m_Dim = Color.Lerp(m_BaseColor, Color.black, 0.35f);
            RefreshVisual();
        }

        void OnSelectionChanged(int selected)
        {
            RefreshVisual();
        }

        public void Press()
        {
            var ctrl = ClimbStaminaController.Instance;
            if (ctrl != null)
            {
                ctrl.SetDifficulty(index);
                var cfg = ctrl.config;
                if (cfg != null && cfg.rememberDifficulty)
                    PlayerPrefs.SetInt(ClimbStaminaController.DifficultyPlayerPrefsKey, index);
            }

            var route = DifficultyRouteController.Instance;
            if (route != null) route.SetSelected(index);

            RefreshVisual();
        }

        public void RefreshVisual()
        {
            if (m_Background == null) return;

            int selected = index;
            var route = DifficultyRouteController.Instance;
            if (route != null)
            {
                selected = route.SelectedIndex;
            }
            else
            {
                var ctrl = ClimbStaminaController.Instance;
                if (ctrl != null) selected = ctrl.DifficultyIndex;
            }

            m_Background.color = selected == index ? m_Bright : m_Dim;
        }
    }
}