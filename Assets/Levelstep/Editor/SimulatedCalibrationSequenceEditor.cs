using UnityEditor;
using UnityEngine;

namespace Levelstep.Editor
{
    [CustomEditor(typeof(SimulatedCalibrationSequence))]
    public class SimulatedCalibrationSequenceEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            var sequence = (SimulatedCalibrationSequence)target;
            if (EditorGUI.EndChangeCheck() && Application.isPlaying) sequence.RefreshConfiguration();
            EditorGUILayout.HelpBox("Timed simulation only: no measurements or gameplay calculation changes. The configured button opens stage 1; each stage requires I'm Ready. Additional Stages follow the first instruction/duration. Only one calibration can be open; finish or cancel it before opening another. Initial visibility only opens instructions, never a countdown.", MessageType.Info);
            if (!Application.isPlaying && sequence.panel != null)
            {
                for (int i = 0; i < sequence.StageCount; i++)
                {
                    int stage = i;
                    if (GUILayout.Button("Preview Stage " + (stage + 1) + " Instructions / Ready"))
                        CalibrationPanelViewEditor.Edit(sequence.panel, () => sequence.panel.ShowReady(sequence.GetStageInstruction(stage)));
                }
            }
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Run Demo")) sequence.BeginCalibration();
                if (GUILayout.Button("I'm Ready")) sequence.ConfirmReady();
                if (GUILayout.Button("Skip")) sequence.SkipCalibration();
                if (GUILayout.Button("Cancel / Close")) sequence.CancelCalibration();
            }
            if (Application.isPlaying)
                EditorGUILayout.LabelField("Simulation state", "Stage " + (sequence.CurrentStageIndex + 1) + "/" + sequence.StageCount + ": " + sequence.CurrentPhase + " / " + sequence.LastOutcome);
        }
    }
}
