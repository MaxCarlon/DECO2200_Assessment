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

        [Header("Selection border")]
        [SerializeField] Color selectedBorderColor = Color.white;
        [Tooltip("Outline thickness in the menu canvas's UI units.")]
        [SerializeField] Vector2 selectedBorderThickness = new Vector2(6f, 6f);

        Image m_Background;
        Outline m_SelectionOutline;
        Color m_BaseColor;
        Color m_Bright;
        Color m_Dim;

        void OnEnable()
        {
            DifficultyRouteController.SelectionChanged += OnSelectionChanged;
            RefreshVisual();
        }

        void OnDisable()
        {
            DifficultyRouteController.SelectionChanged -= OnSelectionChanged;
        }

        void Start()
        {
            m_Background = GetComponent<Image>();
            if (m_Background != null)
            {
                m_BaseColor = m_Background.color;
                m_SelectionOutline = GetComponent<Outline>() ?? gameObject.AddComponent<Outline>();
                m_SelectionOutline.effectColor = selectedBorderColor;
                m_SelectionOutline.effectDistance = selectedBorderThickness;
                m_SelectionOutline.useGraphicAlpha = false;
                m_SelectionOutline.enabled = false;
            }

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

            int selected = -1;
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

            bool isSelected = selected == index;
            m_Background.color = isSelected ? m_Bright : m_Dim;
            if (m_SelectionOutline != null)
                m_SelectionOutline.enabled = isSelected;
        }
    }
}
