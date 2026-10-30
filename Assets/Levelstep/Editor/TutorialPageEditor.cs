using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Levelstep.Editor
{
    [CustomEditor(typeof(TutorialPage))]
    public class TutorialPageEditor : UnityEditor.Editor
    {
        bool m_ShowLegacy;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
            DrawPropertiesExcluding(serializedObject, "m_Script", "showTitleInFullImage", "fullImagePadding");
            if (serializedObject.FindProperty("layoutMode").enumValueIndex == (int)TutorialPage.LayoutMode.FullImage)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Full Image options", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("showTitleInFullImage"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("fullImagePadding"), true);
                EditorGUILayout.HelpBox("Full Image fits the complete sprite without cropping or distortion. Resize/move Full Image Viewport in the Scene view; padding changes the rendered image only. If showing the title, leave header room with Top padding or the viewport bounds.", MessageType.Info);
            }
            bool changed = EditorGUI.EndChangeCheck();
            changed |= serializedObject.ApplyModifiedProperties();
            if (changed) RefreshOpenPanels((TutorialPage)target);

            var page = (TutorialPage)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Frame folder (editor only)", EditorStyles.boldLabel);
            var folder = string.IsNullOrEmpty(page.FrameFolderPath) ? null : AssetDatabase.LoadAssetAtPath<DefaultAsset>(page.FrameFolderPath);
            var next = (DefaultAsset)EditorGUILayout.ObjectField("Source folder", folder, typeof(DefaultAsset), false);
            if (next != folder)
            {
                string path = AssetDatabase.GetAssetPath(next);
                if (next == null || AssetDatabase.IsValidFolder(path))
                {
                    Undo.RecordObject(page, "Set tutorial frame folder");
                    page.FrameFolderPath = path;
                    EditorUtility.SetDirty(page);
                }
            }
            using (new EditorGUI.DisabledScope(!AssetDatabase.IsValidFolder(page.FrameFolderPath)))
                if (GUILayout.Button("Populate Frames From Folder (Natural Order)")) PopulateFrames(page);
            if (GUILayout.Button("Refresh Open Tutorial Previews")) RefreshOpenPanels(page);
            if (GUILayout.Button("Preview This Page in Open Tutorials"))
            {
                foreach (var panel in UnityEngine.Object.FindObjectsByType<TutorialPanel>(FindObjectsInactive.Include))
                {
                    int index = panel.pages != null ? panel.pages.IndexOf(page) : -1;
                    if (index >= 0) TutorialPanelEditor.Edit(panel, () => panel.PreviewPage(index));
                }
            }
            EditorGUILayout.HelpBox("Folder import reads existing Sprite sub-assets, including subfolders. It never changes images or import settings. You can also drag sprites directly into Image > Frames. Text and layout stay editable independently.", MessageType.Info);
            EditorGUILayout.HelpBox("Content Appearance controls image rendering and text styling only. Image + Text and Full Image have independent Scene-authored image areas; switching pages/modes never rewrites their RectTransforms.", MessageType.Info);
            m_ShowLegacy = EditorGUILayout.Foldout(m_ShowLegacy, "Legacy page layout values (read-only, not applied)", true);
            if (m_ShowLegacy)
            {
                serializedObject.Update();
                using (new EditorGUI.DisabledScope(true))
                    foreach (string name in new[] { "imagePosition", "imageSize", "titlePosition", "titleSize", "textPosition", "textSize" })
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("layout." + name), true);
            }
        }

        public static Sprite[] LoadFrames(string folder)
        {
            var paths = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            paths.Sort(EditorUtility.NaturalCompare);
            var result = new List<Sprite>();
            foreach (string path in paths)
            {
                var sprites = new List<Sprite>();
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (asset is Sprite sprite) sprites.Add(sprite);
                sprites.Sort((a, b) => EditorUtility.NaturalCompare(a.name, b.name));
                result.AddRange(sprites);
            }
            return result.ToArray();
        }

        public static void PopulateFrames(TutorialPage page)
        {
            if (page == null || !AssetDatabase.IsValidFolder(page.FrameFolderPath)) return;
            var frames = LoadFrames(page.FrameFolderPath);
            if (frames.Length == 0)
            {
                Debug.LogWarning("[Tutorial] No imported Sprites found. Import the images as Sprite (2D and UI) first; existing frames were kept.", page);
                return;
            }
            Undo.RecordObject(page, "Populate tutorial frames");
            if (page.image == null) page.image = new ImageSequenceSettings();
            page.image.frames = frames;
            EditorUtility.SetDirty(page);
            RefreshOpenPanels(page);
            Debug.Log("[Tutorial] Assigned " + frames.Length + " naturally ordered frames to " + page.name + ".", page);
        }

        static void RefreshOpenPanels(TutorialPage page)
        {
            foreach (var panel in UnityEngine.Object.FindObjectsByType<TutorialPanel>(FindObjectsInactive.Include))
            {
                if (panel.CurrentPage != page) continue;
                TutorialPanelEditor.Edit(panel, panel.RefreshConfiguration);
            }
            SceneView.RepaintAll();
        }

        [MenuItem("Assets/Create/Levelstep/Tutorial Page From Selected Folder")]
        static void CreateFromFolder()
        {
            string folder = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (!AssetDatabase.IsValidFolder(folder)) return;
            string path = EditorUtility.SaveFilePanelInProject("Create tutorial page", Path.GetFileName(folder) + "Page", "asset", "Choose where to save the reusable page.");
            if (string.IsNullOrEmpty(path)) return;
            var page = CreateInstance<TutorialPage>();
            page.pageTitle = ObjectNames.NicifyVariableName(Path.GetFileName(folder));
            page.FrameFolderPath = folder;
            AssetDatabase.CreateAsset(page, path);
            PopulateFrames(page);
            AssetDatabase.SaveAssets();
            Selection.activeObject = page;
        }

        [MenuItem("Assets/Create/Levelstep/Tutorial Page From Selected Folder", true)]
        static bool CanCreateFromFolder() => AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(Selection.activeObject));
    }
}
