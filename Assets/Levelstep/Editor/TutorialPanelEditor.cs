using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Levelstep.Editor
{
    [CustomEditor(typeof(TutorialPanel))]
    public class TutorialPanelEditor : UnityEditor.Editor
    {
        bool m_ShowBindings;
        bool m_ShowLegacy;
        ReorderableList m_Pages;

        void OnEnable()
        {
            m_Pages = new ReorderableList(serializedObject, serializedObject.FindProperty("pages"), true, true, true, true);
            m_Pages.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Pages — gameplay navigation order");
            m_Pages.elementHeight = EditorGUIUtility.singleLineHeight + 6f;
            m_Pages.drawElementCallback = (rect, index, active, focused) =>
            {
                rect.y += 3f;
                rect.height = EditorGUIUtility.singleLineHeight;
                EditorGUI.PropertyField(rect, m_Pages.serializedProperty.GetArrayElementAtIndex(index), new GUIContent("Page " + (index + 1)));
            };
            m_Pages.onAddCallback = list =>
            {
                int index = list.serializedProperty.arraySize;
                list.serializedProperty.arraySize++;
                list.serializedProperty.GetArrayElementAtIndex(index).objectReferenceValue = null;
                list.index = index;
            };
            m_Pages.onRemoveCallback = list =>
            {
                int index = list.index;
                if (index < 0 || index >= list.serializedProperty.arraySize) return;
                int count = list.serializedProperty.arraySize;
                list.serializedProperty.DeleteArrayElementAtIndex(index);
                if (list.serializedProperty.arraySize == count) list.serializedProperty.DeleteArrayElementAtIndex(index);
                list.index = Mathf.Min(index, list.serializedProperty.arraySize - 1);
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
            m_Pages.DoLayoutList();
            EditorGUILayout.HelpBox("Drag the handle at the left of a page row to reorder it. + adds an empty slot; − removes only the reference, never the page asset. Save the scene (or prefab in Prefab Mode) to keep the order.", MessageType.Info);
            DrawPropertiesExcluding(serializedObject, "m_Script", "pages");
            bool changed = EditorGUI.EndChangeCheck();
            changed |= serializedObject.ApplyModifiedProperties();
            var panel = (TutorialPanel)target;
            if (changed && Application.isPlaying) panel.RefreshConfiguration();
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Scene view owns layout: edit any child RectTransform with the Rect Tool. Configuration, preview and page changes preserve positions, sizes, anchors, pivots, rotation and scale. Edit outside Play Mode and save to keep your layout. Initial visibility/page take effect when Play starts.", MessageType.Info);
            if (GUILayout.Button("Apply Configuration (Content & Appearance Only)")) Edit(panel, panel.RefreshConfiguration);
            if (GUILayout.Button("Preview Initial Page (Show Panel)")) Edit(panel, panel.PreviewInitialPage);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Previous")) Edit(panel, panel.PreviousPage);
                if (GUILayout.Button("Next")) Edit(panel, panel.NextPage);
                if (GUILayout.Button(panel.IsVisible ? "Hide" : "Show")) Edit(panel, panel.ToggleVisibility);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Restart Animation")) panel.RestartAnimation();
                if (GUILayout.Button("Pause")) panel.PauseAnimation();
                if (GUILayout.Button("Resume")) panel.ResumeAnimation();
            }
            m_ShowBindings = EditorGUILayout.Foldout(m_ShowBindings, "Advanced: authored UI references", true);
            if (m_ShowBindings)
            {
                serializedObject.Update();
                EditorGUILayout.PropertyField(serializedObject.FindProperty("view"), true);
                serializedObject.ApplyModifiedProperties();
            }
            m_ShowLegacy = EditorGUILayout.Foldout(m_ShowLegacy, "Legacy layout values (read-only, not applied)", true);
            if (m_ShowLegacy)
            {
                EditorGUILayout.HelpBox("Earlier serialized layout values are retained so existing settings are not lost. These are compatibility data only; the RectTransforms are authoritative.", MessageType.None);
                using (new EditorGUI.DisabledScope(true))
                {
                    foreach (string name in new[] { "panelPose", "panelSize", "togglePose", "toggleSize", "counterPosition", "counterSize", "previous.position", "previous.size", "next.position", "next.size", "defaultLayout.imagePosition", "defaultLayout.imageSize", "defaultLayout.titlePosition", "defaultLayout.titleSize", "defaultLayout.textPosition", "defaultLayout.textSize" })
                        EditorGUILayout.PropertyField(serializedObject.FindProperty(name), true);
                }
            }
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(Application.isPlaying))
                if (GUILayout.Button("Reset Layout to Defaults…") && EditorUtility.DisplayDialog("Reset tutorial layout?", "Reset the tutorial's existing UI RectTransforms to their creation defaults? World placement, page content, ordering and appearance settings will be kept. You can Undo this action.", "Reset Layout", "Cancel"))
                    Edit(panel, () => LevelstepTutorialTools.ResetLayoutToDefaults(panel));
        }

        internal static void Edit(TutorialPanel panel, System.Action action)
        {
            if (!Application.isPlaying) Undo.RegisterFullObjectHierarchyUndo(panel.gameObject, "Edit tutorial preview");
            action();
            if (!Application.isPlaying)
            {
                foreach (var component in panel.GetComponentsInChildren<Component>(true))
                {
                    if (component == null) continue;
                    EditorUtility.SetDirty(component);
                    if (PrefabUtility.IsPartOfPrefabInstance(component))
                        PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                }
                if (panel.gameObject.scene.IsValid())
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
            }
            SceneView.RepaintAll();
        }
    }
}
