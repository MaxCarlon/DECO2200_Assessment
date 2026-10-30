using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Levelstep
{
    [Serializable]
    public sealed class CalibrationPanelBindings
    {
        public RectTransform panelRoot;
        public Image background;
        public TMP_Text title;
        public TMP_Text prototypeLabel;
        public TMP_Text message;
        public TMP_Text countdown;
        public Image progressBack;
        public Image progressFill;
        public Button skipButton;
        public Button cancelButton;
        public Button readyButton;
    }

    /// <summary>
    /// Reusable presentation only: a future reach calibration may drive this same UI.
    /// All geometry belongs to authored RectTransforms, never to this component.
    /// </summary>
    [DisallowMultipleComponent]
    public class CalibrationPanelView : MonoBehaviour
    {
        [Header("Headings and labels")]
        public string title = "BALANCE CALIBRATION";
        public string prototypeLabel = "";
        public string countdownFormat = "{0}";
        public string skipLabel = "SKIP";
        public string cancelLabel = "CANCEL";
        public string closeLabel = "CLOSE";

        [Header("Text appearance (geometry: Scene view / RectTransforms)")]
        public TMP_FontAsset font;
        [Min(1f)] public float titleFontSize = 42f;
        [Min(1f)] public float prototypeFontSize = 24f;
        [Min(1f)] public float messageFontSize = 42f;
        [Min(1f)] public float countdownFontSize = 88f;
        public Color titleColor = Color.white;
        public Color prototypeColor = new Color(0.7f, 0.8f, 0.85f);
        public Color instructionColor = Color.white;
        public Color completionColor = new Color(0.5f, 1f, 0.7f);
        public Color countdownColor = Color.white;

        [Header("Panel and progress")]
        public Color backgroundColor = new Color(0.055f, 0.08f, 0.11f);
        [Range(0f, 1f)] public float backgroundOpacity = 0.96f;
        public Color progressBackColor = new Color(0.12f, 0.18f, 0.23f);
        public Color progressFillColor = new Color(0.5f, 1f, 0.7f);

        [Header("Buttons")]
        public TutorialButtonAppearance buttonAppearance = new TutorialButtonAppearance { fontSize = 32f };

        [Header("Ready button")]
        public string readyLabel = "I'm Ready";
        public TutorialButtonAppearance readyButtonAppearance = new TutorialButtonAppearance { fontSize = 36f };

        [SerializeField, HideInInspector] CalibrationPanelBindings view = new CalibrationPanelBindings();
        bool m_Complete;
        int m_LastSeconds = int.MinValue;
        string m_LastFormat;
        public CalibrationPanelBindings View => view;
        public bool IsVisible => view.panelRoot != null && view.panelRoot.gameObject.activeSelf;

        public void RefreshAppearance()
        {
            ApplyText(view.title, titleFontSize, titleColor);
            ApplyText(view.prototypeLabel, prototypeFontSize, prototypeColor);
            ApplyText(view.message, messageFontSize, m_Complete ? completionColor : instructionColor);
            ApplyText(view.countdown, countdownFontSize, countdownColor);
            SetText(view.title, title);
            SetText(view.prototypeLabel, prototypeLabel);
            if (view.prototypeLabel != null) view.prototypeLabel.gameObject.SetActive(!string.IsNullOrEmpty(prototypeLabel));
            if (view.background != null)
            {
                var color = backgroundColor;
                color.a = backgroundOpacity;
                view.background.color = color;
                view.background.raycastTarget = false;
            }
            if (view.progressBack != null) { view.progressBack.color = progressBackColor; view.progressBack.raycastTarget = false; }
            if (view.progressFill != null) { view.progressFill.color = progressFillColor; view.progressFill.raycastTarget = false; }
            StyleButton(view.skipButton, skipLabel);
            StyleButton(view.cancelButton, m_Complete ? closeLabel : cancelLabel);
            StyleButton(view.readyButton, readyLabel, readyButtonAppearance);
            m_LastSeconds = int.MinValue;
        }

        public void ShowReady(string instruction)
        {
            m_Complete = false;
            RefreshAppearance();
            SetText(view.message, instruction);
            if (view.countdown != null) view.countdown.gameObject.SetActive(false);
            if (view.progressBack != null) view.progressBack.gameObject.SetActive(false);
            if (view.skipButton != null) { view.skipButton.interactable = false; view.skipButton.gameObject.SetActive(false); }
            if (view.readyButton != null) { view.readyButton.interactable = true; view.readyButton.gameObject.SetActive(true); }
            SetProgress(0f);
            SetVisible(true);
        }

        public void ShowInstructions(string instruction, float remainingSeconds, float progress)
        {
            m_Complete = false;
            RefreshAppearance();
            SetText(view.message, instruction);
            if (view.countdown != null) view.countdown.gameObject.SetActive(true);
            if (view.readyButton != null) view.readyButton.gameObject.SetActive(false);
            if (view.progressBack != null) view.progressBack.gameObject.SetActive(true);
            if (view.skipButton != null) { view.skipButton.gameObject.SetActive(true); view.skipButton.interactable = true; }
            SetVisible(true);
            UpdateCountdown(remainingSeconds, progress);
        }

        public void UpdateCountdown(float remainingSeconds, float progress)
        {
            int seconds = Mathf.CeilToInt(Mathf.Max(0f, remainingSeconds));
            if (seconds != m_LastSeconds || m_LastFormat != countdownFormat)
            {
                m_LastSeconds = seconds;
                m_LastFormat = countdownFormat;
                string text;
                try { text = string.Format(countdownFormat ?? "{0}", seconds); }
                catch (FormatException) { text = seconds.ToString(); }
                SetText(view.countdown, text);
            }
            SetProgress(progress);
        }

        public void ShowComplete(string message)
        {
            m_Complete = true;
            RefreshAppearance();
            SetText(view.message, message);
            if (view.countdown != null) view.countdown.gameObject.SetActive(false);
            if (view.readyButton != null) view.readyButton.gameObject.SetActive(false);
            if (view.progressBack != null) view.progressBack.gameObject.SetActive(true);
            if (view.skipButton != null) { view.skipButton.gameObject.SetActive(true); view.skipButton.interactable = false; }
            SetProgress(1f);
            SetVisible(true);
        }

        public void Hide() => SetVisible(false);

        void SetVisible(bool visible)
        {
            if (view.panelRoot == null) return;
            if (!visible && EventSystem.current != null)
            {
                var selected = EventSystem.current.currentSelectedGameObject;
                if (selected != null && selected.transform.IsChildOf(view.panelRoot)) EventSystem.current.SetSelectedGameObject(null);
            }
            if (view.panelRoot.gameObject.activeSelf != visible) view.panelRoot.gameObject.SetActive(visible);
        }

        void SetProgress(float value)
        {
            if (view.progressFill != null && !Mathf.Approximately(view.progressFill.fillAmount, Mathf.Clamp01(value)))
                view.progressFill.fillAmount = Mathf.Clamp01(value);
        }

        void ApplyText(TMP_Text text, float size, Color color)
        {
            if (text == null) return;
            text.fontSize = size;
            text.color = color;
            text.raycastTarget = false;
            if (font != null) text.font = font;
        }

        static void SetText(TMP_Text text, string value)
        {
            if (text != null && text.text != value) text.text = value;
        }

        void StyleButton(Button button, string label, TutorialButtonAppearance appearance = null)
        {
            var style = appearance ?? buttonAppearance;
            if (button == null || style == null) return;
            var image = button.GetComponent<Image>();
            if (image != null) image.color = Color.white;
            var colors = button.colors;
            colors.normalColor = style.normal;
            colors.highlightedColor = style.hovered;
            colors.pressedColor = style.pressed;
            colors.selectedColor = style.selected;
            colors.disabledColor = style.disabled;
            colors.colorMultiplier = 1f;
            button.colors = colors;
            var text = button.GetComponentInChildren<TMP_Text>(true);
            ApplyText(text, style.fontSize, style.textColor);
            SetText(text, label);
            var outline = button.GetComponent<Outline>();
            if (outline != null)
            {
                outline.effectColor = style.borderColor;
                outline.effectDistance = style.borderThickness;
                outline.useGraphicAlpha = false;
            }
        }
    }
}
