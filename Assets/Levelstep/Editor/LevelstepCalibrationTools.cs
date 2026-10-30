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
    /// <summary>Explicit, additive setup only. Existing calibration UI/layout is never rebuilt.</summary>
    public static class LevelstepCalibrationTools
    {
        public const string PrefabPath = "Assets/Levelstep/Prefabs/SimulatedBoardCalibration.prefab";

        [MenuItem("Tools/Levelstep/Calibration/Add Simulated Board Calibration")]
        public static void AddToActiveScene()
        {
            if (Application.isPlaying) return;
            var scene = SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var existing in root.GetComponentsInChildren<SimulatedCalibrationSequence>(true))
                {
                    if (existing.GetComponent<ReachCalibrationSettings>() != null) continue;
                    Selection.activeGameObject = existing.gameObject;
                    return;
                }
            }
            var prefab = EnsurePrefab();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Undo.RegisterCreatedObjectUndo(instance, "Add simulated board calibration");
            instance.name = "Simulated Balance Calibration (Prototype)";
            var camera = Camera.main;
            if (camera != null)
            {
                // A scene-authored parent/pose, not a runtime follow/positioning script.
                instance.transform.SetParent(camera.transform, false);
                instance.transform.localPosition = new Vector3(0f, -0.05f, 1.2f);
                instance.transform.localRotation = Quaternion.identity;
            }
            else instance.transform.position = new Vector3(0f, 1.6f, 1.2f);
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            Selection.activeGameObject = instance;
            EditorSceneManager.MarkSceneDirty(scene);
        }

        public static void IntegrateClimbingScene()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Levelstep/Scenes/ClimbingScene.unity");
            AddToActiveScene();
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[SimulatedCalibrationSetup] Prototype calibration integrated with left secondary button activation; no sensor calibration wiring.");
        }

        public static GameObject EnsurePrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null) return existing;
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            var sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            if (font == null || sprite == null) throw new InvalidOperationException("Existing TMP font or built-in UI sprite is unavailable.");
            var root = new GameObject("Simulated Board Calibration");
            try
            {
                var panel = root.AddComponent<CalibrationPanelView>();
                panel.font = font;
                var sequence = root.AddComponent<SimulatedCalibrationSequence>();
                sequence.panel = panel;
                var ui = panel.View;
                ui.panelRoot = Rect("Calibration Panel", root.transform, Vector2.zero, new Vector2(1000f, 640f));
                ui.panelRoot.localScale = Vector3.one * 0.001f;
                var canvas = ui.panelRoot.gameObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                var raycaster = ui.panelRoot.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
                raycaster.ignoreReversedGraphics = false;
                raycaster.checkFor2DOcclusion = false;
                raycaster.checkFor3DOcclusion = false;
                ui.background = Image("Background", ui.panelRoot, Vector2.zero, Vector2.zero);
                Stretch(ui.background.rectTransform);
                ui.title = Text("Title", ui.panelRoot, new Vector2(0f, 250f), new Vector2(900f, 70f), font);
                ui.prototypeLabel = Text("Prototype Label", ui.panelRoot, new Vector2(0f, 198f), new Vector2(900f, 40f), font);
                ui.message = Text("Instructions and Completion", ui.panelRoot, new Vector2(0f, 95f), new Vector2(900f, 110f), font);
                ui.countdown = Text("Countdown", ui.panelRoot, new Vector2(0f, -30f), new Vector2(300f, 110f), font);
                ui.progressBack = Image("Progress Back", ui.panelRoot, new Vector2(0f, -120f), new Vector2(800f, 22f));
                ui.progressFill = Image("Progress Fill", ui.progressBack.transform, Vector2.zero, Vector2.zero);
                Stretch(ui.progressFill.rectTransform);
                ui.progressFill.sprite = sprite;
                ui.progressFill.type = UnityEngine.UI.Image.Type.Filled;
                ui.progressFill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
                ui.progressFill.fillOrigin = 0;
                ui.progressFill.fillAmount = 0f;
                ui.skipButton = Button("Skip", ui.panelRoot, new Vector2(-220f, -230f), font);
                ui.cancelButton = Button("Cancel Close", ui.panelRoot, new Vector2(220f, -230f), font);
                UnityEventTools.AddPersistentListener(ui.skipButton.onClick, sequence.SkipCalibration);
                UnityEventTools.AddPersistentListener(ui.cancelButton.onClick, sequence.CancelCalibration);
                EnsureReadyControl(sequence, false);
                panel.ShowReady(sequence.instructionText);
                // Authored UI stays inspectable in Edit Mode; sequence.Awake always hides it at runtime.
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [MenuItem("Tools/Levelstep/Calibration/Add Ready Button (Preserve Layout)")]
        public static void UpgradeReadyControls()
        {
            if (Application.isPlaying) return;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
            {
                var root = PrefabUtility.LoadPrefabContents(PrefabPath);
                try
                {
                    if (EnsureReadyControl(root.GetComponent<SimulatedCalibrationSequence>(), false))
                        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            foreach (var sequence in Object.FindObjectsByType<SimulatedCalibrationSequence>(FindObjectsInactive.Include))
            {
                if (!EnsureReadyControl(sequence, true)) continue;
                foreach (var component in sequence.GetComponentsInChildren<Component>(true))
                    if (component != null && PrefabUtility.IsPartOfPrefabInstance(component))
                        PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                if (sequence.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(sequence.gameObject.scene);
            }
        }

        public static void UpgradeReadyControlsInClimbingScene()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Levelstep/Scenes/ClimbingScene.unity");
            UpgradeReadyControls();
            EditorSceneManager.SaveScene(scene);
        }

        static bool EnsureReadyControl(SimulatedCalibrationSequence sequence, bool undo)
        {
            if (sequence == null || sequence.panel == null || sequence.panel.View.panelRoot == null) return false;
            var panel = sequence.panel;
            var ui = panel.View;
            bool changed = false;
            if (ui.readyButton == null)
            {
                if (undo) Undo.RecordObject(panel, "Add calibration Ready button");
                var existing = ui.panelRoot.Find("I'm Ready");
                ui.readyButton = existing != null ? existing.GetComponent<Button>() : null;
                if (ui.readyButton == null)
                {
                    ui.readyButton = Button("I'm Ready", ui.panelRoot, new Vector2(0f, -30f), panel.font);
                    if (undo) Undo.RegisterCreatedObjectUndo(ui.readyButton.gameObject, "Add calibration Ready button");
                    // Initial placement of the NEW control only. Existing RectTransforms are untouched.
                    if (ui.countdown != null)
                    {
                        var source = ui.countdown.rectTransform;
                        var target = (RectTransform)ui.readyButton.transform;
                        target.anchorMin = source.anchorMin;
                        target.anchorMax = source.anchorMax;
                        target.pivot = source.pivot;
                        target.anchoredPosition3D = source.anchoredPosition3D;
                        target.localRotation = source.localRotation;
                        target.localScale = source.localScale;
                        target.sizeDelta = source.sizeDelta;
                        if (source.anchorMin == source.anchorMax)
                            target.sizeDelta = new Vector2(Mathf.Max(400f, source.rect.width), Mathf.Max(90f, source.rect.height));
                    }
                    ui.readyButton.gameObject.SetActive(false);
                }
                EditorUtility.SetDirty(panel);
                changed = true;
            }
            bool wired = false;
            for (int i = 0; i < ui.readyButton.onClick.GetPersistentEventCount(); i++)
                wired |= ui.readyButton.onClick.GetPersistentTarget(i) == sequence && ui.readyButton.onClick.GetPersistentMethodName(i) == nameof(SimulatedCalibrationSequence.ConfirmReady);
            if (!wired)
            {
                if (undo) Undo.RecordObject(ui.readyButton, "Wire calibration Ready button");
                UnityEventTools.AddPersistentListener(ui.readyButton.onClick, sequence.ConfirmReady);
                EditorUtility.SetDirty(ui.readyButton);
                changed = true;
            }
            if (!string.IsNullOrEmpty(panel.prototypeLabel))
            {
                if (undo) Undo.RecordObject(panel, "Hide calibration prototype caption");
                panel.prototypeLabel = "";
                EditorUtility.SetDirty(panel);
                changed = true;
            }
            if (ui.prototypeLabel != null && (ui.prototypeLabel.gameObject.activeSelf || !string.IsNullOrEmpty(ui.prototypeLabel.text)))
            {
                if (undo) Undo.RecordObjects(new Object[] { ui.prototypeLabel, ui.prototypeLabel.gameObject }, "Hide calibration prototype caption");
                ui.prototypeLabel.text = "";
                ui.prototypeLabel.gameObject.SetActive(false);
                EditorUtility.SetDirty(ui.prototypeLabel);
                EditorUtility.SetDirty(ui.prototypeLabel.gameObject);
                changed = true;
            }
            if (changed) panel.RefreshAppearance();
            return changed;
        }

        static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        static Image Image(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var image = Rect(name, parent, position, size).gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        static TMP_Text Text(string name, Transform parent, Vector2 position, Vector2 size, TMP_FontAsset font)
        {
            var text = Rect(name, parent, position, size).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.enableAutoSizing = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }

        static Button Button(string name, Transform parent, Vector2 position, TMP_FontAsset font)
        {
            var image = Image(name, parent, position, new Vector2(310f, 70f));
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.Explicit };
            image.gameObject.AddComponent<Outline>();
            var text = Text("Label", image.transform, Vector2.zero, Vector2.zero, font);
            Stretch(text.rectTransform);
            return button;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
