using System;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Object = UnityEngine.Object;

namespace Levelstep.Editor
{
    public static class LevelstepCompletionTools
    {
        public const string PrefabPath = "Assets/Levelstep/Prefabs/ClimbCompletionScreen.prefab";

        [MenuItem("Tools/Levelstep/Completion/Add or Select Climb Completion Screen")]
        public static void AddToActiveScene()
        {
            if (Application.isPlaying) return;
            var scene = SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                var existing = root.GetComponentInChildren<ClimbCompletionScreen>(true);
                if (existing == null) continue;
                Selection.activeGameObject = existing.gameObject;
                return; // Never replace authored geometry, settings, or app-event wiring.
            }
            var camera = Camera.main;
            var timer = Object.FindAnyObjectByType<RouteTimer>();
            if (camera == null || timer == null) throw new InvalidOperationException("Completion screen needs the existing Main Camera and RouteTimer.");
            var prefab = EnsurePrefab();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Undo.RegisterCreatedObjectUndo(instance, "Add climb completion screen");
            instance.name = "Climb Completion Screen";
            instance.transform.SetParent(camera.transform, false);
            var screen = instance.GetComponent<ClimbCompletionScreen>();
            screen.routeTimer = timer;
            PrefabUtility.RecordPrefabInstancePropertyModifications(screen);
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            Selection.activeGameObject = instance;
            EditorSceneManager.MarkSceneDirty(scene);
        }

        public static void IntegrateClimbingScene()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Levelstep/Scenes/ClimbingScene.unity");
            AddToActiveScene();
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[CompletionSetup] Success-only, scene-authored completion panel integrated with existing tracked UI; optional hand stamina defaults off.");
        }

        public static GameObject EnsurePrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null) return existing;
            var root = new GameObject("Climb Completion Screen");
            try
            {
                var screen = root.AddComponent<ClimbCompletionScreen>();
                var style = FindCalibrationStyle();
                CopyCalibrationAppearance(screen, style);
                screen.overlayMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Levelstep/Materials/LevelstepHUD.mat");
                screen.textOverlayShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/TextMesh Pro/Shaders/TMP_SDF-Mobile Overlay.shader");
                if (screen.textFont == null || screen.overlayMaterial == null || screen.textOverlayShader == null) throw new InvalidOperationException("Existing TMP font/overlay shader/HUD material unavailable.");
                var ui = screen.View;
                ui.panelRoot = Rect("Completion Panel", root.transform, Vector2.zero, new Vector2(1000, 560));
                ui.panelRoot.localPosition = new Vector3(0f, -0.05f, 1f);
                ui.panelRoot.localScale = Vector3.one * 0.0008f;
                var canvas = ui.panelRoot.gameObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace; canvas.overrideSorting = true; canvas.sortingOrder = 200;
                var raycaster = ui.panelRoot.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
                raycaster.ignoreReversedGraphics = false; raycaster.checkFor2DOcclusion = false; raycaster.checkFor3DOcclusion = false;
                ui.background = Image("Background", ui.panelRoot, Vector2.zero, new Vector2(1000, 560), screen.overlayMaterial);
                ui.background.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"); ui.background.type = UnityEngine.UI.Image.Type.Sliced;
                ui.title = Text("Title", ui.panelRoot, new Vector2(0, 175), new Vector2(920, 90), screen);
                ui.description = Text("Description", ui.panelRoot, new Vector2(0, 35), new Vector2(900, 180), screen);
                ui.noButton = Button("No Thanks", ui.panelRoot, new Vector2(-230, -175), screen);
                ui.yesButton = Button("Yes Open Attempt", ui.panelRoot, new Vector2(230, -175), screen);
                UnityEventTools.AddPersistentListener(ui.yesButton.onClick, screen.ChooseOpenAttempt);
                UnityEventTools.AddPersistentListener(ui.noButton.onClick, screen.ChooseNoThanks);
                screen.PreviewInEditor(); // Authored UI is visible/editable; Awake always hides it at launch.
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [MenuItem("Tools/Levelstep/Completion/Match Calibration Style (Preserve Layout)")]
        public static void RestyleActiveScene()
        {
            if (Application.isPlaying) return;
            var style = FindCalibrationStyle();
            foreach (var screen in Object.FindObjectsByType<ClimbCompletionScreen>(FindObjectsInactive.Include))
            {
                Undo.RegisterFullObjectHierarchyUndo(screen.gameObject, "Match completion UI to calibration");
                RestyleExisting(screen, style);
                foreach (var component in screen.GetComponentsInChildren<Component>(true))
                {
                    if (component == null) continue;
                    EditorUtility.SetDirty(component);
                    if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                }
                EditorSceneManager.MarkSceneDirty(screen.gameObject.scene);
            }
        }

        public static void RestyleClimbingScene()
        {
            EditorSceneManager.OpenScene("Assets/Levelstep/Scenes/ClimbingScene.unity");
            var style = FindCalibrationStyle();
            // Upgrade the existing asset in-place. RectTransforms, objects and Button events
            // keep their identity; only legacy Text components become TMP components.
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                RestyleExisting(root.GetComponent<ClimbCompletionScreen>(), style);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            RestyleActiveScene();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Debug.Log("[CompletionStyle] Reused calibration TMP font/palette/buttons; 0.5-second delay; No left / Yes right. Existing UI geometry and events preserved.");
        }

        static CalibrationPanelView FindCalibrationStyle()
        {
            foreach (var panel in Object.FindObjectsByType<CalibrationPanelView>(FindObjectsInactive.Include))
                if (panel.GetComponent<ReachCalibrationSettings>() == null) return panel;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LevelstepCalibrationTools.PrefabPath);
            var fallback = prefab != null ? prefab.GetComponent<CalibrationPanelView>() : null;
            if (fallback == null) throw new InvalidOperationException("Existing calibration styling was not found.");
            return fallback;
        }

        static void CopyCalibrationAppearance(ClimbCompletionScreen screen, CalibrationPanelView source)
        {
            screen.textFont = source.font;
            screen.titleColor = source.titleColor; screen.descriptionColor = source.instructionColor;
            screen.titleFontSize = Mathf.RoundToInt(source.titleFontSize); screen.descriptionFontSize = Mathf.RoundToInt(source.messageFontSize);
            var background = source.backgroundColor; background.a = source.backgroundOpacity; screen.backgroundColor = background;
            screen.yesAppearance = JsonUtility.FromJson<TutorialButtonAppearance>(JsonUtility.ToJson(source.buttonAppearance));
            screen.noAppearance = JsonUtility.FromJson<TutorialButtonAppearance>(JsonUtility.ToJson(source.buttonAppearance));
        }

        static void RestyleExisting(ClimbCompletionScreen screen, CalibrationPanelView source)
        {
            if (screen == null || screen.View.panelRoot == null) throw new InvalidOperationException("Existing completion UI bindings are missing.");
            CopyCalibrationAppearance(screen, source);
            screen.appearanceDelay = 0.5f;
            screen.textOverlayShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/TextMesh Pro/Shaders/TMP_SDF-Mobile Overlay.shader");
            var ui = screen.View;
            ui.title = UpgradeText(ui.title, ui.panelRoot.Find("Title"), screen.textFont);
            ui.description = UpgradeText(ui.description, ui.panelRoot.Find("Description"), screen.textFont);
            foreach (var button in new[] { ui.noButton, ui.yesButton })
            {
                if (button == null) continue;
                var label = button.GetComponentInChildren<TMP_Text>(true) as Graphic ?? button.GetComponentInChildren<UnityEngine.UI.Text>(true);
                UpgradeText(label, button.transform.Find("Label"), screen.textFont);
            }
            if (ui.background != null && source.View.background != null)
            { ui.background.sprite = source.View.background.sprite; ui.background.type = source.View.background.type; }
            if (ui.noButton != null && ui.yesButton != null)
            {
                var no = (RectTransform)ui.noButton.transform; var yes = (RectTransform)ui.yesButton.transform;
                Vector2 middle = (no.anchoredPosition + yes.anchoredPosition) * 0.5f;
                float halfSeparation = Mathf.Abs(no.anchoredPosition.x - yes.anchoredPosition.x) * 0.5f;
                // One-time authored row adjustment only; re-running never flips an already
                // corrected row. Existing sizes, row centre, separation and callbacks stay intact.
                no.anchoredPosition = new Vector2(middle.x - halfSeparation, middle.y);
                yes.anchoredPosition = new Vector2(middle.x + halfSeparation, middle.y);
            }
            screen.RefreshAppearance();
        }

        struct TextGeometry
        {
            public Vector2 min, max, pivot, size;
            public Vector3 position, scale;
            public Quaternion rotation;
            public TextGeometry(RectTransform rect)
            { min = rect.anchorMin; max = rect.anchorMax; pivot = rect.pivot; size = rect.sizeDelta; position = rect.anchoredPosition3D; scale = rect.localScale; rotation = rect.localRotation; }
            public void Restore(RectTransform rect)
            { rect.anchorMin = min; rect.anchorMax = max; rect.pivot = pivot; rect.sizeDelta = size; rect.anchoredPosition3D = position; rect.localScale = scale; rect.localRotation = rotation; }
        }

        static Graphic UpgradeText(Graphic graphic, Transform fallback, TMP_FontAsset font)
        {
            var go = graphic != null ? graphic.gameObject : fallback != null ? fallback.gameObject : null;
            if (go == null) throw new InvalidOperationException("Authored completion text object is missing.");
            var rect = go.GetComponent<RectTransform>(); var geometry = new TextGeometry(rect);
            var legacy = go.GetComponent<UnityEngine.UI.Text>();
            if (legacy != null) Object.DestroyImmediate(legacy);
            var text = go.GetComponent<TextMeshProUGUI>();
            if (text == null) text = go.AddComponent<TextMeshProUGUI>();
            text.font = font; text.fontSharedMaterial = font.material;
            text.enableAutoSizing = false; text.fontStyle = FontStyles.Normal;
            text.textWrappingMode = TextWrappingModes.Normal; text.overflowMode = TextOverflowModes.Overflow;
            text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false;
            geometry.Restore(rect);
            return text;
        }

        static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
            rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
        }
        static Image Image(string name, Transform parent, Vector2 position, Vector2 size, Material material)
        {
            var image = Rect(name, parent, position, size).gameObject.AddComponent<Image>();
            image.raycastTarget = false; image.material = material; return image;
        }
        static TMP_Text Text(string name, Transform parent, Vector2 position, Vector2 size, ClimbCompletionScreen screen)
        {
            var text = Rect(name, parent, position, size).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = screen.textFont; text.alignment = TextAlignmentOptions.Center; text.enableAutoSizing = false;
            text.textWrappingMode = TextWrappingModes.Normal; text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false; return text;
        }
        static Button Button(string name, Transform parent, Vector2 position, ClimbCompletionScreen screen)
        {
            var image = Image(name, parent, position, new Vector2(420, 100), screen.overlayMaterial);
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.Explicit };
            image.gameObject.AddComponent<Outline>();
            var label = Text("Label", image.transform, Vector2.zero, Vector2.zero, screen);
            label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            return button;
        }
    }
}
