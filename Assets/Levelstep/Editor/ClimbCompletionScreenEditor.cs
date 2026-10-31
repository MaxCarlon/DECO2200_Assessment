using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Levelstep.Editor
{
    [CustomEditor(typeof(ClimbCompletionScreen))]
    public class ClimbCompletionScreenEditor : UnityEditor.Editor
    {
        bool m_ShowBindings;
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            var screen = (ClimbCompletionScreen)target;
            if (EditorGUI.EndChangeCheck() && Application.isPlaying) screen.RefreshAppearance();
            EditorGUILayout.HelpBox("Layout is Scene-authored: No is on the left, Yes on the right. Use the Rect Tool or placement fields below; runtime and appearance refresh never rewrite geometry. TMP font/palette/button values were copied from calibration and remain editable here. Appearance Delay defaults to 0.5 seconds after successful completion; it does not wait for the HUD result to disappear. Yes retains its existing optional event/placeholder action.", MessageType.Info);
            var panel = screen.View.panelRoot;
            if (panel != null)
            {
                EditorGUILayout.LabelField("Scene-authored panel placement", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                var position = EditorGUILayout.Vector3Field("Local Position", panel.localPosition);
                var rotation = EditorGUILayout.Vector3Field("Local Rotation", panel.localEulerAngles);
                var scale = EditorGUILayout.Vector3Field("Local Scale", panel.localScale);
                var size = EditorGUILayout.Vector2Field("Panel Size", panel.sizeDelta);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(panel, "Edit completion panel placement");
                    panel.localPosition = position; panel.localEulerAngles = rotation; panel.localScale = scale; panel.sizeDelta = size;
                    EditorUtility.SetDirty(panel);
                    if (PrefabUtility.IsPartOfPrefabInstance(panel)) PrefabUtility.RecordPrefabInstancePropertyModifications(panel);
                    EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
                }
                if (GUILayout.Button("Select Panel for Scene Rect Tool")) Selection.activeTransform = panel;
            }
            if (GUILayout.Button("Apply Appearance (Keep Layout)")) Edit(screen, screen.RefreshAppearance);
            if (!Application.isPlaying)
            {
                if (GUILayout.Button("Preview Completion Screen")) Edit(screen, screen.PreviewInEditor);
                if (GUILayout.Button("Hide Preview")) Edit(screen, screen.CancelAndHide);
            }
            else
            {
                EditorGUILayout.LabelField("Pending / Visible", screen.IsPending + " / " + screen.IsVisible);
                EditorGUILayout.LabelField("Completed Local Run", screen.CompletedRunSequence.ToString());
                EditorGUILayout.LabelField("Last Response", screen.LastResponse.ToString());
                if (GUILayout.Button("Dismiss / Cancel Pending")) screen.CancelAndHide();
            }
            m_ShowBindings = EditorGUILayout.Foldout(m_ShowBindings, "Advanced: authored UI references", true);
            if (m_ShowBindings)
            {
                serializedObject.Update(); EditorGUILayout.PropertyField(serializedObject.FindProperty("view"), true); serializedObject.ApplyModifiedProperties();
            }
        }

        static void Edit(ClimbCompletionScreen screen, System.Action action)
        {
            Undo.RegisterFullObjectHierarchyUndo(screen.gameObject, "Edit completion screen appearance/preview");
            action();
            foreach (var component in screen.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                EditorUtility.SetDirty(component);
                if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }
            EditorSceneManager.MarkSceneDirty(screen.gameObject.scene);
            SceneView.RepaintAll();
        }
    }
}
