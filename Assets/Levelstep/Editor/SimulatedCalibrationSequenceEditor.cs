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
            EditorGUILayout.HelpBox("Prototype timer simulation only: no sensor measurements or balance calculation changes. Y opens instructions; select I'm Ready to start the countdown. Press activation again to cancel. This component always starts idle.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Run Demo")) sequence.BeginCalibration();
                if (GUILayout.Button("I'm Ready")) sequence.ConfirmReady();
                if (GUILayout.Button("Skip")) sequence.SkipCalibration();
                if (GUILayout.Button("Cancel / Close")) sequence.CancelCalibration();
            }
            if (Application.isPlaying)
                EditorGUILayout.LabelField("Simulation state", sequence.CurrentPhase + " / " + sequence.LastOutcome);
        }
    }
}
