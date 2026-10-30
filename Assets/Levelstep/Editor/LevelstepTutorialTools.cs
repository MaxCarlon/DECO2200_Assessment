using System;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Object = UnityEngine.Object;

namespace Levelstep.Editor
{
    public static class LevelstepTutorialTools
    {
        public const string PrefabPath = "Assets/Levelstep/Prefabs/LevelstepTutorial.prefab";
        public const string PageFolder = "Assets/Levelstep/Settings/TutorialPages";
        const string AnimationFolder = "Assets/Levelstep/TutorialAnimations";

        [MenuItem("Tools/Levelstep/Tutorial/Add Tutorial to Active Scene")]
        public static void AddToActiveScene()
        {
            if (Application.isPlaying) return;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                var existing = root.GetComponentInChildren<TutorialPanel>(true);
                if (existing == null) continue;
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("[Tutorial] Existing tutorial selected; no content or layout was overwritten.");
                return;
            }
            var prefab = EnsurePrefab();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Undo.RegisterCreatedObjectUndo(instance, "Add Levelstep tutorial");
            instance.name = "Levelstep Tutorial";
            instance.transform.SetPositionAndRotation(new Vector3(1.65f, 1.6f, 1.85f), Quaternion.Euler(0f, 42f, 0f));
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            Selection.activeGameObject = instance;
            EditorSceneManager.MarkSceneDirty(scene);
            if (Object.FindAnyObjectByType<EventSystem>() == null || Object.FindAnyObjectByType<XRUIInputModule>() == null)
                Debug.LogWarning("[Tutorial] This scene needs an EventSystem with XRUIInputModule and an XR UI pointer. ClimbingScene already supplies these; no second input system was created.");
        }

        /// <summary>One-time, explicit integration using Unity's serializer. Does not regenerate existing gameplay.</summary>
        public static void IntegrateClimbingScene()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Levelstep/Scenes/ClimbingScene.unity");
            AddToActiveScene();
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[TutorialSetup] Integration complete: " + PrefabPath);
        }

        [MenuItem("Tools/Levelstep/Tutorial/Create Assets and Prefab If Missing")]
        public static void CreateAssetsMenu() => EnsurePrefab();

        public static GameObject EnsurePrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null) return existing;
            EnsureFolder(PageFolder);
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            if (font == null) throw new InvalidOperationException("Existing TextMeshPro font resources could not be loaded.");
            var pages = new[]
            {
                EnsurePage("01_Controllers", "Controller instructions", null, AnimationFolder + "/SimpleControls.png"),
                EnsurePage("02_Push", "Climbing / Pushing", AnimationFolder + "/Push", null),
                EnsurePage("03_Paths", "Climbing paths", AnimationFolder + "/Paths", null),
                EnsurePage("04_Board", "Balance board", AnimationFolder + "/Board", null)
            };
            var root = new GameObject("Levelstep Tutorial");
            try
            {
                var controller = root.AddComponent<TutorialPanel>();
                controller.defaultFont = font;
                controller.pages.AddRange(pages);
                var view = controller.View;
                view.imagePlayer = root.AddComponent<ImageSequencePlayer>();
                view.panelRoot = CreateCanvas("Tutorial Panel", root.transform);
                view.background = CreateImage("Background", view.panelRoot);
                Stretch(view.background.rectTransform);
                view.title = CreateText("Page Title", view.panelRoot, font);
                view.description = CreateText("Description", view.panelRoot, font);
                view.imageViewport = CreateRect("Image Viewport", view.panelRoot);
                view.imageViewport.gameObject.AddComponent<RectMask2D>();
                var image = CreateImage("Frame Image", view.imageViewport);
                Stretch(image.rectTransform);
                image.gameObject.AddComponent<ImageSequenceCrop>().enabled = false;
                view.imagePlayer.Bind(image, null);
                view.pageCounter = CreateText("Page Counter", view.panelRoot, font);
                view.previousButton = CreateButton("Previous", view.panelRoot, font);
                view.nextButton = CreateButton("Next", view.panelRoot, font);
                view.toggleRoot = CreateCanvas("Tutorial Toggle (Always Accessible)", root.transform);
                view.toggleButton = CreateButton("Show Hide Tutorial", view.toggleRoot, font);
                Stretch((RectTransform)view.toggleButton.transform);
                view.toggleLabel = view.toggleButton.GetComponentInChildren<TMP_Text>();
                UnityEventTools.AddPersistentListener(view.previousButton.onClick, controller.PreviousPage);
                UnityEventTools.AddPersistentListener(view.nextButton.onClick, controller.NextPage);
                UnityEventTools.AddPersistentListener(view.toggleButton.onClick, controller.ToggleVisibility);
                ResetLayoutToDefaults(controller);
                controller.PreviewInitialPage();
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [MenuItem("Tools/Levelstep/Tutorial/Upgrade Image Rendering (Preserve Layout)")]
        public static void UpgradeExistingImageRendering()
        {
            if (Application.isPlaying) return;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
            {
                var root = PrefabUtility.LoadPrefabContents(PrefabPath);
                try
                {
                    var panel = root.GetComponent<TutorialPanel>();
                    if (UpgradeImageRendering(panel, false)) PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            foreach (var panel in Object.FindObjectsByType<TutorialPanel>(FindObjectsInactive.Include))
            {
                if (!UpgradeImageRendering(panel, true)) continue;
                EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
            }
        }

        static bool UpgradeImageRendering(TutorialPanel panel, bool undo)
        {
            if (panel == null || panel.View.imagePlayer == null || panel.View.imagePlayer.TargetImage == null) return false;
            var player = panel.View.imagePlayer;
            var image = player.TargetImage;
            bool changed = false;
            var fitter = image.GetComponent<AspectRatioFitter>();
            if (fitter != null && fitter.enabled)
            {
                if (undo) Undo.RecordObject(fitter, "Stop tutorial layout driving");
                fitter.enabled = false;
                EditorUtility.SetDirty(fitter);
                if (PrefabUtility.IsPartOfPrefabInstance(fitter)) PrefabUtility.RecordPrefabInstancePropertyModifications(fitter);
                changed = true;
            }
            var crop = image.GetComponent<ImageSequenceCrop>();
            if (crop == null)
            {
                crop = undo ? Undo.AddComponent<ImageSequenceCrop>(image.gameObject) : image.gameObject.AddComponent<ImageSequenceCrop>();
                crop.enabled = player.scalingMode == ImageSequencePlayer.ScalingMode.FillAndCrop;
                changed = true;
            }
            var serialized = new SerializedObject(player);
            var property = serialized.FindProperty("cropEffect");
            if (property.objectReferenceValue != crop)
            {
                property.objectReferenceValue = crop;
                serialized.ApplyModifiedProperties();
                changed = true;
            }
            return changed;
        }

        /// <summary>Only for initial creation or the explicit, Undoable Inspector reset. Never called at runtime.</summary>
        public static void ResetLayoutToDefaults(TutorialPanel controller)
        {
            var view = controller.View;
            var layout = new TutorialPageLayout();
            SetSurface(view.panelRoot, Vector3.zero, new Vector2(1600f, 1000f));
            SetSurface(view.toggleRoot, new Vector3(0f, -0.65f, 0f), new Vector2(560f, 110f));
            if (view.background != null) StretchDefault(view.background.rectTransform);
            SetRect(view.imageViewport, layout.imagePosition, layout.imageSize);
            if (view.title != null) SetRect(view.title.rectTransform, layout.titlePosition, layout.titleSize);
            if (view.description != null) SetRect(view.description.rectTransform, layout.textPosition, layout.textSize);
            if (view.pageCounter != null) SetRect(view.pageCounter.rectTransform, new Vector2(0f, -410f), new Vector2(280f, 80f));
            ResetButton(view.previousButton, new Vector2(-535f, -410f));
            ResetButton(view.nextButton, new Vector2(535f, -410f));
            if (view.toggleButton != null)
            {
                StretchDefault((RectTransform)view.toggleButton.transform);
                var label = view.toggleButton.GetComponentInChildren<TMP_Text>(true);
                if (label != null) StretchDefault(label.rectTransform);
            }
            if (view.imagePlayer != null && view.imagePlayer.TargetImage != null)
                StretchDefault(view.imagePlayer.TargetImage.rectTransform);
        }

        static void ResetButton(Button button, Vector2 position)
        {
            if (button == null) return;
            SetRect((RectTransform)button.transform, position, new Vector2(320f, 100f));
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null) StretchDefault(label.rectTransform);
        }

        static void SetSurface(RectTransform rect, Vector3 position, Vector2 size)
        {
            if (rect == null) return;
            SetRect(rect, Vector2.zero, size);
            rect.localPosition = position;
            rect.localScale = Vector3.one * 0.001f;
        }

        static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
        {
            if (rect == null) return;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
            rect.anchoredPosition3D = position;
            rect.sizeDelta = size;
        }

        static void StretchDefault(RectTransform rect)
        {
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            rect.pivot = Vector2.one * 0.5f;
            rect.anchoredPosition3D = Vector3.zero;
            Stretch(rect);
        }

        static TutorialPage EnsurePage(string name, string title, string folder, string staticImage)
        {
            string path = PageFolder + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<TutorialPage>(path);
            if (existing != null) return existing;
            var page = ScriptableObject.CreateInstance<TutorialPage>();
            page.pageTitle = title;
            // Descriptions are intentionally blank: the author supplies instructional copy.
            if (folder != null)
            {
                page.FrameFolderPath = folder;
                page.image.frames = TutorialPageEditor.LoadFrames(folder);
                if (page.image.frames.Length == 0) throw new InvalidOperationException("No Sprite frames found in " + folder);
            }
            if (staticImage != null)
            {
                page.image.staticImage = AssetDatabase.LoadAssetAtPath<Sprite>(staticImage);
                if (page.image.staticImage == null) throw new InvalidOperationException("No Sprite found at " + staticImage);
            }
            AssetDatabase.CreateAsset(page, path);
            return page;
        }

        static RectTransform CreateCanvas(string name, Transform parent)
        {
            var rect = CreateRect(name, parent);
            var canvas = rect.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var raycaster = rect.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
            raycaster.ignoreReversedGraphics = false;
            raycaster.checkFor2DOcclusion = false;
            raycaster.checkFor3DOcclusion = false;
            return rect;
        }

        static RectTransform CreateRect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        static Image CreateImage(string name, Transform parent)
        {
            var image = CreateRect(name, parent).gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        static TMP_Text CreateText(string name, Transform parent, TMP_FontAsset font)
        {
            var text = CreateRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.enableAutoSizing = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }

        static Button CreateButton(string name, Transform parent, TMP_FontAsset font)
        {
            var image = CreateImage(name, parent);
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            // Pointer clicks can retain Selected feedback, without auto-linking into other menus.
            button.navigation = new Navigation { mode = Navigation.Mode.Explicit };
            image.gameObject.AddComponent<Outline>();
            var label = CreateText("Label", image.transform, font);
            Stretch(label.rectTransform);
            return button;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
