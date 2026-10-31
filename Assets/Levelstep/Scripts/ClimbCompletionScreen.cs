using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Levelstep
{
    [Serializable]
    public sealed class CompletionScreenBindings
    {
        public RectTransform panelRoot;
        public Image background;
        public Graphic title;
        public Graphic description;
        public Button yesButton;
        public Button noButton;
    }

    /// <summary>Success-only presentation. All placement/size/rotation belongs to authored RectTransforms.</summary>
    [DisallowMultipleComponent]
    public class ClimbCompletionScreen : MonoBehaviour
    {
        [Header("Behaviour")]
        public bool enableCompletionScreen = true;
        [Min(0f)] public float appearanceDelay = 0.5f;
        public bool useUnscaledTime = true;
        public RouteTimer routeTimer;

        [Header("Content")]
        public string title = "Climb Complete!";
        [TextArea] public string description = "Would you like to view your latest attempt on the LevelStep app?";
        public string yesLabel = "Yes, Open Attempt";
        public string noLabel = "No Thanks";

        [Header("Appearance (geometry: Scene view / RectTransforms)")]
        [HideInInspector] public Font font; // Legacy reference retained for existing serialized screens.
        [InspectorName("Font (Tutorial / Calibration)")] public TMP_FontAsset textFont;
        [Min(1)] public int titleFontSize = 42;
        [Min(1)] public int descriptionFontSize = 42;
        public Color titleColor = Color.white;
        public Color descriptionColor = Color.white;
        public Color backgroundColor = new Color(0.055f, 0.08f, 0.11f, 0.96f);
        public TutorialButtonAppearance yesAppearance = new TutorialButtonAppearance { fontSize = 32f };
        public TutorialButtonAppearance noAppearance = new TutorialButtonAppearance { fontSize = 32f };
        [Tooltip("Existing HUD material for panel/button images only. TMP text keeps its font's SDF shader/atlas instead of using this bitmap-UI shader.")]
        public Material overlayMaterial;
        [Tooltip("Existing TMP mobile SDF overlay variant preserves post-wall visibility without changing shared font materials.")]
        public Shader textOverlayShader;

        [Header("Optional app integration")]
        [Tooltip("Assign a future app bridge's public method here. No endpoint, attempt ID or deep link is assumed. Snapshot properties expose the completed local run.")]
        public UnityEvent onOpenAttemptRequested = new UnityEvent();
        [Tooltip("Demo placeholder: log the request only. Disable this log when wiring an actual integration action.")]
        public bool logPlaceholderRequest = true;

        [SerializeField, HideInInspector] CompletionScreenBindings view = new CompletionScreenBindings();
        public CompletionScreenBindings View => view;
        public bool IsVisible => view.panelRoot != null && view.panelRoot.gameObject.activeSelf;
        public bool IsPending { get; private set; }
        public float CompletedElapsedSeconds { get; private set; }
        public int CompletedRouteIndex { get; private set; } = -1;
        /// <summary>Scene-local sequence only; never a fabricated mobile-app attempt ID.</summary>
        public int CompletedRunSequence { get; private set; } = -1;
        public enum Response { None, OpenAttempt, NoThanks }
        public Response LastResponse { get; private set; }

        RouteTimer m_BoundTimer;
        int m_LastHandledRun = -1;
        float m_RemainingDelay;
        bool m_PresentingCompletion;
        readonly Dictionary<Graphic, Material> m_Materials = new Dictionary<Graphic, Material>();
        Material m_MaterialTemplate;
        TMP_FontAsset m_FontTemplate;
        Shader m_TextShaderTemplate;

        void Awake()
        {
            CancelAndHide(); // An Edit Mode preview never becomes an automatic launch popup.
            RefreshAppearance();
        }

        void OnEnable() { if (Application.isPlaying) BindTimer(); }
        void Start() => BindTimer();
        void OnDisable() { UnbindTimer(); CancelAndHide(); }
        void OnDestroy() { UnbindTimer(); ReleaseMaterials(); }

        void BindTimer()
        {
            if (routeTimer == null) routeTimer = RouteTimer.Instance != null ? RouteTimer.Instance : Object.FindAnyObjectByType<RouteTimer>();
            if (m_BoundTimer == routeTimer) return;
            UnbindTimer();
            m_LastHandledRun = -1;
            m_BoundTimer = routeTimer;
            if (m_BoundTimer == null) return;
            m_BoundTimer.RunStarted += OnRunStarted;
            m_BoundTimer.RunEnded += OnRunEnded;
        }

        void UnbindTimer()
        {
            if (m_BoundTimer != null)
            {
                m_BoundTimer.RunStarted -= OnRunStarted;
                m_BoundTimer.RunEnded -= OnRunEnded;
            }
            m_BoundTimer = null;
        }

        void OnRunStarted() => CancelAndHide();

        void OnRunEnded(float elapsed, bool success)
        {
            if (m_BoundTimer == null || (success && m_BoundTimer.CurrentState != RouteTimer.State.Finished) ||
                (!success && m_BoundTimer.CurrentState != RouteTimer.State.Aborted) || m_LastHandledRun == m_BoundTimer.RunSequence) return;
            m_LastHandledRun = m_BoundTimer.RunSequence;
            CancelAndHide();
            if (!success || !enableCompletionScreen || !isActiveAndEnabled || m_BoundTimer.CurrentState != RouteTimer.State.Finished) return;
            CompletedRunSequence = m_BoundTimer.RunSequence;
            CompletedElapsedSeconds = elapsed;
            CompletedRouteIndex = DifficultyRouteController.Instance != null ? DifficultyRouteController.Instance.SelectedIndex :
                ClimbStaminaController.Instance != null ? ClimbStaminaController.Instance.DifficultyIndex : -1;
            LastResponse = Response.None;
            m_RemainingDelay = Mathf.Max(0f, appearanceDelay);
            IsPending = true;
        }

        void Update()
        {
            if (routeTimer != m_BoundTimer) { CancelAndHide(); BindTimer(); }
            if (!enableCompletionScreen) { if (IsPending || IsVisible) CancelAndHide(); return; }
            if (!IsPending) return;
            if (m_BoundTimer == null || m_BoundTimer.RunSequence != CompletedRunSequence || m_BoundTimer.CurrentState != RouteTimer.State.Finished)
            { CancelAndHide(); return; }
            m_RemainingDelay -= useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            if (m_RemainingDelay > 0f) return;
            IsPending = false;
            RefreshAppearance();
            m_PresentingCompletion = true;
            SetVisible(true);
        }

        public void ChooseOpenAttempt()
        {
            if (!Application.isPlaying || !m_PresentingCompletion || !IsVisible) return;
            LastResponse = Response.OpenAttempt;
            CancelAndHide(); // Close before calling an external action, even if that action fails.
            if (logPlaceholderRequest)
                Debug.Log($"[Levelstep Completion] Open latest attempt requested (placeholder only). Local run {CompletedRunSequence}, route {CompletedRouteIndex}, elapsed {CompletedElapsedSeconds:F2}s. Configure On Open Attempt Requested to connect the app.", this);
            onOpenAttemptRequested?.Invoke();
        }

        public void ChooseNoThanks()
        {
            if (!Application.isPlaying || !m_PresentingCompletion || !IsVisible) return;
            LastResponse = Response.NoThanks;
            CancelAndHide();
        }

        public void CancelAndHide()
        {
            IsPending = false;
            m_PresentingCompletion = false;
            SetVisible(false);
        }

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

        public void RefreshAppearance()
        {
            StyleText(view.title, title, titleFontSize, titleColor);
            StyleText(view.description, description, descriptionFontSize, descriptionColor);
            if (view.background != null) { view.background.color = backgroundColor; view.background.raycastTarget = false; }
            StyleButton(view.yesButton, yesLabel, yesAppearance);
            StyleButton(view.noButton, noLabel, noAppearance);
            if (Application.isPlaying)
            {
                if (m_MaterialTemplate != overlayMaterial || m_FontTemplate != textFont || m_TextShaderTemplate != textOverlayShader)
                { ReleaseMaterials(); m_MaterialTemplate = overlayMaterial; m_FontTemplate = textFont; m_TextShaderTemplate = textOverlayShader; }
                ApplyMaterial(view.background); ApplyMaterial(view.title); ApplyMaterial(view.description);
                ApplyButtonMaterials(view.yesButton); ApplyButtonMaterials(view.noButton);
            }
            if (Application.isPlaying && !enableCompletionScreen) CancelAndHide();
            // Never change parent/position/rotation/scale/anchors/pivots/sizes here or in Update.
        }

        void StyleText(Graphic graphic, string value, int size, Color color)
        {
            if (graphic == null) return;
            graphic.color = color; graphic.raycastTarget = false;
            if (graphic is TMP_Text tmp)
            {
                if (tmp.text != value) tmp.text = value;
                tmp.fontSize = Mathf.Max(1, size);
                if (textFont != null && tmp.font != textFont) tmp.font = textFont;
            }
            else if (graphic is Text legacy)
            {
                // Compatibility until the explicit editor upgrade converts old text in-place.
                if (legacy.text != value) legacy.text = value;
                legacy.fontSize = Mathf.Max(1, size);
                if (font != null) legacy.font = font;
            }
        }

        void StyleButton(Button button, string label, TutorialButtonAppearance appearance)
        {
            if (button == null || appearance == null) return;
            var image = button.GetComponent<Image>(); if (image != null) image.color = Color.white;
            var colors = button.colors;
            colors.normalColor = appearance.normal; colors.highlightedColor = appearance.hovered;
            colors.pressedColor = appearance.pressed; colors.selectedColor = appearance.selected;
            colors.disabledColor = appearance.disabled; colors.colorMultiplier = 1f;
            button.colors = colors;
            StyleText(ButtonLabel(button), label, Mathf.RoundToInt(appearance.fontSize), appearance.textColor);
            var outline = button.GetComponent<Outline>();
            if (outline != null) { outline.effectColor = appearance.borderColor; outline.effectDistance = appearance.borderThickness; outline.useGraphicAlpha = false; }
        }

        void ApplyButtonMaterials(Button button)
        {
            if (button == null) return;
            ApplyMaterial(button.GetComponent<Image>()); ApplyMaterial(ButtonLabel(button));
        }

        static Graphic ButtonLabel(Button button)
        {
            var tmp = button.GetComponentInChildren<TMP_Text>(true);
            return tmp != null ? (Graphic)tmp : button.GetComponentInChildren<Text>(true);
        }

        void ApplyMaterial(Graphic graphic)
        {
            if (graphic == null) return;
            if (graphic is TMP_Text tmp)
            {
                if (tmp.font == null) return;
                if (!m_Materials.TryGetValue(graphic, out var textMaterial))
                {
                    // Clone the *font* material so the SDF atlas, face colour and weight stay
                    // consistent with calibration/tutorial. Never apply LevelstepHUD to TMP.
                    textMaterial = new Material(tmp.font.material) { name = tmp.font.material.name + " (Completion " + graphic.name + ")" };
                    if (textOverlayShader != null) textMaterial.shader = textOverlayShader;
                    m_Materials.Add(graphic, textMaterial);
                }
                tmp.fontSharedMaterial = textMaterial;
                return;
            }
            if (overlayMaterial == null) { graphic.material = null; return; }
            if (!m_Materials.TryGetValue(graphic, out var material))
            {
                material = new Material(overlayMaterial) { name = overlayMaterial.name + " (Completion " + graphic.name + ")" };
                m_Materials.Add(graphic, material);
            }
            graphic.material = material;
        }

        void ReleaseMaterials()
        {
            foreach (var entry in m_Materials)
            {
                if (entry.Key is TMP_Text tmp) { if (tmp != null && tmp.font != null) tmp.fontSharedMaterial = tmp.font.material; }
                else if (entry.Key != null) entry.Key.material = overlayMaterial;
                if (entry.Value != null) Destroy(entry.Value);
            }
            m_Materials.Clear();
        }

#if UNITY_EDITOR
        public void PreviewInEditor()
        {
            if (Application.isPlaying) return;
            RefreshAppearance(); SetVisible(true);
        }
#endif
    }
}
