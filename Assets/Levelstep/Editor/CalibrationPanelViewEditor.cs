using UnityEditor;
using UnityEngine;

namespace Levelstep.Editor
{
    [CustomEditor(typeof(CalibrationPanelView))]
    public class CalibrationPanelViewEditor : UnityEditor.Editor
    {
        bool m_ShowBindings;

        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            var panel = (CalibrationPanelView)target;
            if (EditorGUI.EndChangeCheck() && Application.isPlaying) panel.RefreshAppearance();
            EditorGUILayout.HelpBox("Layout belongs to the Scene view's RectTransforms. Move the root Transform to position the entire panel; its scene instance is parented to Main Camera. Appearance and previews never reposition or resize UI. Previewing in Edit Mode does not start a simulation.", MessageType.Info);
            if (GUILayout.Button("Apply Appearance (Keep Layout)")) Edit(panel, panel.RefreshAppearance);
            if (!Application.isPlaying)
            {
                var sequence = panel.GetComponent<SimulatedCalibrationSequence>();
                if (GUILayout.Button("Preview Instructions / Ready")) Edit(panel, () => panel.ShowReady(
                    sequence != null ? sequence.instructionText : "Calibration instructions"));
                if (GUILayout.Button("Preview Countdown")) Edit(panel, () => panel.ShowInstructions(
                    sequence != null ? sequence.instructionText : "Calibration instructions", sequence != null ? sequence.calibrationDuration : 3f, 0f));
                if (GUILayout.Button("Preview Completion")) Edit(panel, () => panel.ShowComplete(sequence != null ? sequence.completionText : "Calibration Complete"));
                if (GUILayout.Button("Hide Preview")) Edit(panel, panel.Hide);
            }
            m_ShowBindings = EditorGUILayout.Foldout(m_ShowBindings, "Advanced: authored UI references", true);
            if (m_ShowBindings)
            {
                serializedObject.Update();
                EditorGUILayout.PropertyField(serializedObject.FindProperty("view"), true);
                serializedObject.ApplyModifiedProperties();
            }
        }

        internal static void Edit(CalibrationPanelView panel, System.Action action)
        {
            Undo.RegisterFullObjectHierarchyUndo(panel.gameObject, "Edit calibration preview");
            action();
            foreach (var component in panel.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                EditorUtility.SetDirty(component);
                if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }
            if (panel.gameObject.scene.IsValid()) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
            SceneView.RepaintAll();
        }
    }
}
