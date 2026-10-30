using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Levelstep
{
    [Serializable]
    public sealed class TutorialSurfacePose
    {
        [Tooltip("Metres relative to the stationary Tutorial root. Move that root Transform to change world placement.")]
        public Vector3 localPosition;
        public Vector3 localEulerAngles;
        public Vector3 localScale = Vector3.one * 0.001f;
    }

    [Serializable]
    public sealed class TutorialButtonLayout
    {
        public string label;
        [HideInInspector] public Vector2 position;
        [HideInInspector] public Vector2 size = new Vector2(320f, 100f);
    }

    [Serializable]
    public sealed class TutorialButtonAppearance
    {
        public Color normal = new Color(0.12f, 0.22f, 0.27f);
        public Color hovered = new Color(0.2f, 0.48f, 0.48f);
        public Color pressed = new Color(0.4f, 0.7f, 0.5f);
        public Color selected = new Color(0.2f, 0.55f, 0.38f);
        public Color disabled = new Color(0.16f, 0.18f, 0.2f, 0.5f);
        public Color textColor = Color.white;
        [Min(1f)] public float fontSize = 38f;
        public Color borderColor = new Color(0.85f, 1f, 0.95f);
        public Vector2 borderThickness = new Vector2(4f, 4f);
    }

    [Serializable]
    public sealed class TutorialViewBindings
    {
        public RectTransform panelRoot;
        public Image background;
        public RectTransform imageViewport;
        public TMP_Text title;
        public TMP_Text description;
        public TMP_Text pageCounter;
        public Button previousButton;
        public Button nextButton;
        public RectTransform toggleRoot;
        public Button toggleButton;
        public TMP_Text toggleLabel;
        public ImageSequencePlayer imagePlayer;
    }

    /// <summary>Stationary authored UI. Only this panel's own controls and content are changed.</summary>
    [DisallowMultipleComponent]
    public class TutorialPanel : MonoBehaviour
    {
        [Header("General")]
        [Tooltip("Drag reusable Tutorial Page assets here; drag list entries to reorder them.")]
        public List<TutorialPage> pages = new List<TutorialPage>();
        public bool initiallyVisible;
        [Min(0)] public int initialPage;
        public bool wrapNavigation;
        public bool restartAnimationOnPageChange = true;
        [Min(0f)] public float defaultAnimationSpeed = 1f;
        [Tooltip("Advance the CURRENT page's animation while hidden. Off-page animations retain their saved position.")]
        public bool playWhileHidden;
        public bool useUnscaledTime = true;

        // Compatibility data only: never applied during startup, navigation or configuration refresh.
        [HideInInspector] public TutorialSurfacePose panelPose = new TutorialSurfacePose();
        [HideInInspector] public Vector2 panelSize = new Vector2(1600f, 1000f);
        [Header("Panel appearance (layout: Scene view / RectTransforms)")]
        public Color backgroundColor = new Color(0.055f, 0.08f, 0.11f);
        [Range(0f, 1f)] public float backgroundOpacity = 0.96f;
        public TMP_FontAsset defaultFont;
        [InspectorName("Default Content Appearance")]
        public TutorialPageLayout defaultLayout = new TutorialPageLayout();

        [Header("Navigation")]
        public TutorialButtonLayout previous = new TutorialButtonLayout { label = "PREVIOUS", position = new Vector2(-535f, -410f) };
        public TutorialButtonLayout next = new TutorialButtonLayout { label = "NEXT", position = new Vector2(535f, -410f) };
        public TutorialButtonAppearance navigationAppearance = new TutorialButtonAppearance();
        public bool showPageCounter = true;
        [HideInInspector] public Vector2 counterPosition = new Vector2(0f, -410f);
        [HideInInspector] public Vector2 counterSize = new Vector2(280f, 80f);
        [Min(1f)] public float counterFontSize = 36f;
        public Color counterColor = Color.white;
        [Tooltip("{0} is the current page (1-based), {1} is the total. Empty lists show 0 / 0.")]
        public string counterFormat = "{0} / {1}";

        [Header("Toggle (separate canvas; remains accessible while hidden)")]
        [HideInInspector] public TutorialSurfacePose togglePose = new TutorialSurfacePose { localPosition = new Vector3(0f, -0.65f, 0f) };
        [HideInInspector] public Vector2 toggleSize = new Vector2(560f, 110f);
        public string showLabel = "SHOW TUTORIAL";
        public string hideLabel = "HIDE TUTORIAL";
        public Color toggleHiddenColor = new Color(0.12f, 0.22f, 0.27f);
        public Color toggleVisibleColor = new Color(0.15f, 0.45f, 0.3f);
        public TutorialButtonAppearance toggleAppearance = new TutorialButtonAppearance();

        [Header("Empty content")]
        public string emptyTitle = "Tutorial";
        [TextArea] public string emptyDescription = "No tutorial page assigned.";

        [SerializeField, HideInInspector] TutorialViewBindings view = new TutorialViewBindings();
        readonly Dictionary<TutorialPage, ImageSequencePlayer.PlaybackState> m_Playback = new Dictionary<TutorialPage, ImageSequencePlayer.PlaybackState>();
        TutorialPage m_CurrentAsset;
        bool m_Initialized;
        public TutorialViewBindings View => view;
        public int CurrentPageIndex { get; private set; } = -1;
        public int PageCount => pages != null ? pages.Count : 0;
        public bool IsVisible { get; private set; }
        public TutorialPage CurrentPage => m_CurrentAsset;

        void Start()
        {
            m_Initialized = false;
            CurrentPageIndex = -1;
            m_CurrentAsset = null;
            m_Playback.Clear();
            RefreshConfiguration();
        }

        void OnValidate()
        {
            initialPage = Mathf.Max(0, initialPage);
            defaultAnimationSpeed = Mathf.Max(0f, defaultAnimationSpeed);
            // UI changes are explicit through the custom inspector, never from OnValidate.
        }

        /// <summary>Refresh content, appearance and behaviour; never write to authored RectTransforms.</summary>
        public void RefreshConfiguration()
        {
            bool visible = m_Initialized ? IsVisible : initiallyVisible;
            int index = m_Initialized ? CurrentPageIndex : initialPage;
            // Reordering references must not switch away from the page currently being previewed.
            if (m_Initialized && m_CurrentAsset != null && pages != null)
            {
                int current = pages.IndexOf(m_CurrentAsset);
                if (current >= 0) index = current;
            }
            ApplyChrome();
            DisplayPage(index, !m_Initialized);
            m_Initialized = true;
            SetVisible(visible);
        }

        public void PreviewInitialPage()
        {
            ApplyChrome();
            DisplayPage(initialPage, true);
            m_Initialized = true;
            SetVisible(true);
        }

        public void ToggleVisibility() => SetVisible(!IsVisible);
        public void Show() => SetVisible(true);
        public void Hide() => SetVisible(false);

        public void SetVisible(bool visible)
        {
            IsVisible = visible;
            if (view.panelRoot != null)
            {
                if (!visible && EventSystem.current != null)
                {
                    var selected = EventSystem.current.currentSelectedGameObject;
                    if (selected != null && selected.transform.IsChildOf(view.panelRoot))
                        EventSystem.current.SetSelectedGameObject(null);
                }
                view.panelRoot.gameObject.SetActive(visible);
            }
            if (view.imagePlayer != null) view.imagePlayer.SetPresentationVisible(visible, playWhileHidden);
            UpdateToggle();
        }

        public void NextPage() => Navigate(1);
        public void PreviousPage() => Navigate(-1);

        public void SetPage(int index)
        {
            if (PageCount == 0) { DisplayPage(-1, true); return; }
            index = Mathf.Clamp(index, 0, PageCount - 1);
            if (index != CurrentPageIndex) DisplayPage(index, restartAnimationOnPageChange);
        }

        public void PauseAnimation() { if (view.imagePlayer != null) view.imagePlayer.Pause(); }
        public void ResumeAnimation() { if (view.imagePlayer != null) view.imagePlayer.Resume(); }
        public void RestartAnimation() { if (view.imagePlayer != null) view.imagePlayer.RestartPlayback(); }

        void Navigate(int delta)
        {
            if (PageCount == 0) return;
            int index = CurrentPageIndex + delta;
            if (wrapNavigation) index = (index % PageCount + PageCount) % PageCount;
            SetPage(index);
        }

        void DisplayPage(int requested, bool restart)
        {
            var player = view.imagePlayer;
            if (m_CurrentAsset != null && player != null)
                m_Playback[m_CurrentAsset] = player.CaptureState();

            CurrentPageIndex = PageCount > 0 ? Mathf.Clamp(requested, 0, PageCount - 1) : -1;
            m_CurrentAsset = CurrentPageIndex >= 0 ? pages[CurrentPageIndex] : null;
            var layout = m_CurrentAsset != null && m_CurrentAsset.overrideLayout ? m_CurrentAsset.layout : defaultLayout;
            layout = layout ?? defaultLayout ?? new TutorialPageLayout();
            var font = layout.fontOverride != null ? layout.fontOverride : defaultFont;
            if (view.title != null)
            {
                view.title.text = m_CurrentAsset != null ? m_CurrentAsset.pageTitle : emptyTitle;
                ApplyTextAppearance(view.title, layout.titleFontSize, layout.titleColor, layout.titleAlignment, font);
            }
            if (view.description != null)
            {
                view.description.text = m_CurrentAsset != null ? m_CurrentAsset.description : emptyDescription;
                ApplyTextAppearance(view.description, layout.textFontSize, layout.textColor, layout.textAlignment, font);
            }
            if (player != null)
            {
                player.speedMultiplier = defaultAnimationSpeed;
                player.useUnscaledTime = useUnscaledTime;
                player.SetScalingMode(layout.imageScaling);
                ImageSequencePlayer.PlaybackState state = default;
                bool resume = !restart && m_CurrentAsset != null && m_Playback.TryGetValue(m_CurrentAsset, out state);
                player.SetSequence(m_CurrentAsset != null ? m_CurrentAsset.image : null, !resume, state);
                player.SetPresentationVisible(IsVisible, playWhileHidden);
            }
            UpdateNavigation();
        }

        void ApplyChrome()
        {
            if (view.background != null)
            {
                var color = backgroundColor;
                color.a = backgroundOpacity;
                view.background.color = color;
                view.background.raycastTarget = false;
            }
            ApplyButton(view.previousButton, previous, navigationAppearance);
            ApplyButton(view.nextButton, next, navigationAppearance);
            if (view.pageCounter != null)
                ApplyTextAppearance(view.pageCounter, counterFontSize, counterColor, TextAlignmentOptions.Center, defaultFont);
        }

        void UpdateNavigation()
        {
            bool multiple = PageCount > 1;
            if (view.previousButton != null) view.previousButton.interactable = multiple && (wrapNavigation || CurrentPageIndex > 0);
            if (view.nextButton != null) view.nextButton.interactable = multiple && (wrapNavigation || CurrentPageIndex < PageCount - 1);
            if (view.pageCounter != null)
            {
                view.pageCounter.gameObject.SetActive(showPageCounter);
                try { view.pageCounter.text = string.Format(counterFormat ?? "{0} / {1}", CurrentPageIndex + 1, PageCount); }
                catch (FormatException) { view.pageCounter.text = (CurrentPageIndex + 1) + " / " + PageCount; }
            }
        }

        void UpdateToggle()
        {
            if (view.toggleButton == null) return;
            ApplyButton(view.toggleButton, null, toggleAppearance);
            var colors = view.toggleButton.colors;
            colors.normalColor = IsVisible ? toggleVisibleColor : toggleHiddenColor;
            view.toggleButton.colors = colors;
            if (view.toggleLabel != null) view.toggleLabel.text = IsVisible ? hideLabel : showLabel;
            var outline = view.toggleButton.GetComponent<Outline>();
            if (outline != null) outline.enabled = IsVisible;
        }

        void ApplyButton(Button button, TutorialButtonLayout layout, TutorialButtonAppearance appearance)
        {
            if (button == null || appearance == null) return;
            var image = button.GetComponent<Image>();
            if (image != null) image.color = Color.white;
            var colors = button.colors;
            colors.normalColor = appearance.normal;
            colors.highlightedColor = appearance.hovered;
            colors.pressedColor = appearance.pressed;
            colors.selectedColor = appearance.selected;
            colors.disabledColor = appearance.disabled;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.1f;
            button.colors = colors;
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                if (layout != null) label.text = layout.label;
                label.fontSize = appearance.fontSize;
                label.color = appearance.textColor;
                label.raycastTarget = false;
                if (defaultFont != null) label.font = defaultFont;
            }
            var outline = button.GetComponent<Outline>();
            if (outline != null)
            {
                outline.effectColor = appearance.borderColor;
                outline.effectDistance = appearance.borderThickness;
                outline.useGraphicAlpha = false;
            }
        }

        static void ApplyTextAppearance(TMP_Text text, float fontSize, Color color, TextAlignmentOptions alignment, TMP_FontAsset font)
        {
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            if (font != null) text.font = font;
        }

    }
}
