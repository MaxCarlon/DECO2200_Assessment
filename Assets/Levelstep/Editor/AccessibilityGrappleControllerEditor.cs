using UnityEditor;
using UnityEngine;

namespace Levelstep.Editor
{
    [CustomEditor(typeof(AccessibilityGrappleController))]
    public class AccessibilityGrappleControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            var controller = (AccessibilityGrappleController)target;
            if (EditorGUI.EndChangeCheck() && Application.isPlaying) controller.RefreshConfiguration();
            EditorGUILayout.HelpBox("X toggles shared Grapple Mode. Point approximately toward nearby holds; Nearest priority and retention make aiming forgiving. Grip locks the displayed target: aim/grace/range changes cannot release it. If a locked hold exceeds range, only its line hides; pulling continues. Grip release, explicit cancellation and real gameplay/collision failures still stop it. Controllers remain exactly tracked. Shared movement speed/settings are on Accessibility Grapple Movement.", MessageType.Info);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.FloatField("Effective Horizontal Reach", controller.HorizontalReach);
                EditorGUILayout.FloatField("Effective Vertical Reach", controller.VerticalReach);
                EditorGUILayout.FloatField("Effective Maximum Distance", controller.MaxDistance);
                EditorGUILayout.FloatField("Effective Search / Grip Range", controller.EffectiveGrappleRange);
            }
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh Target")) controller.RefreshTarget();
                if (GUILayout.Button("Pull (grip held)")) controller.TryGrapple();
                if (GUILayout.Button("Cancel / Release")) controller.ReleaseGrapple();
            }
            if (Application.isPlaying)
            {
                if (GUILayout.Button(controller.GrappleModeEnabled ? "Grapple Mode: ON — turn off" : "Grapple Mode: OFF — turn on"))
                    controller.SetGrappleMode(!controller.GrappleModeEnabled);
                EditorGUILayout.HelpBox(controller.LastFeedback, MessageType.None);
                EditorGUILayout.LabelField("Last Eligibility Rejection", controller.LastRejection.ToString());
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.ObjectField("Locked Hold", controller.LockedTarget,
                        typeof(UnityEngine.XR.Interaction.Toolkit.Locomotion.Climbing.ClimbInteractable), true);
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.ObjectField(controller.IsGrappling ? "Connected hold" : "Target hold",
                        controller.IsGrappling ? controller.SelectionTarget : controller.CurrentTarget,
                        typeof(UnityEngine.XR.Interaction.Toolkit.Locomotion.Climbing.ClimbInteractable), true);
            }
        }
    }
}
