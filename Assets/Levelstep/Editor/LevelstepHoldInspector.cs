using UnityEditor;
using UnityEngine;

namespace Levelstep.Editor
{
    /// <summary>
    /// Inspector for a <see cref="LevelstepHold"/>: model slot (auto-applies), surface offset,
    /// mount orientation tweaks, a per-hold Route (Easy / Moderate / Hard) toggle, a Role (None /
    /// Start / Finish) toggle with an optional marker prefab to slot in, and a Snap to Wall button
    /// that mounts the hold on the nearest surface aligned to its normal.
    /// </summary>
    [CustomEditor(typeof(LevelstepHold))]
    public class LevelstepHoldInspector : UnityEditor.Editor
    {
        SerializedProperty m_Model;
        SerializedProperty m_GenerateColliders;
        SerializedProperty m_SurfaceOffset;
        SerializedProperty m_PitchOffset;
        SerializedProperty m_YawOffset;
        SerializedProperty m_RollOffset;
        SerializedProperty m_Role;
        SerializedProperty m_MarkerPrefab;
        SerializedProperty m_MarkerOffset;
        SerializedProperty m_HoldConfig;

        void OnEnable()
        {
            m_Model = serializedObject.FindProperty("model");
            m_GenerateColliders = serializedObject.FindProperty("generateColliders");
            m_SurfaceOffset = serializedObject.FindProperty("surfaceOffset");
            m_PitchOffset = serializedObject.FindProperty("pitchOffset");
            m_YawOffset = serializedObject.FindProperty("yawOffset");
            m_RollOffset = serializedObject.FindProperty("rollOffset");
            m_Role = serializedObject.FindProperty("role");
            m_MarkerPrefab = serializedObject.FindProperty("markerPrefab");
            m_MarkerOffset = serializedObject.FindProperty("markerOffset");
            m_HoldConfig = serializedObject.FindProperty("config");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(m_Model);
            EditorGUILayout.PropertyField(m_GenerateColliders);
            EditorGUILayout.PropertyField(m_SurfaceOffset);
            EditorGUILayout.PropertyField(m_PitchOffset);
            EditorGUILayout.PropertyField(m_YawOffset);
            EditorGUILayout.PropertyField(m_RollOffset);
            serializedObject.ApplyModifiedProperties();

            var hold = (LevelstepHold)target;

            EditorGUILayout.Space(6);
            DrawRole(hold);

            EditorGUILayout.Space(6);
            DrawRoute(hold);

            EditorGUILayout.Space(6);
            if (GUILayout.Button("Snap to Wall (match face normal)"))
            {
                Undo.RecordObject(hold.transform, "Snap hold to wall");
                Undo.RecordObject(hold, "Snap hold to wall");
                if (ModelTools.SnapHoldToWall(hold))
                    Debug.Log("[Levelstep] '" + hold.name + "' snapped onto the wall surface.");
                else
                    Debug.LogWarning("[Levelstep] No wall surface found near '" + hold.name + "'. Move it closer to the wall and retry.");
            }

            EditorGUILayout.HelpBox(
                "Assigning a Model applies the mesh to this hold automatically.\n" +
                "Mark it Start or Finish to spawn a marker (flag) at runtime and drive the route timer.\n" +
                "Place on a wall: click a wall face in the Scene view (Tools/Levelstep/Snap Hold) " +
                "or press 'Snap to Wall' to mount on the nearest surface, aligned to its normal.",
                MessageType.Info);
        }

        static void DrawRole(LevelstepHold hold)
        {
            AutoFillConfig(hold);

            string[] labels = { "None", "Start", "Finish" };
            int current = Mathf.Clamp((int)hold.role, 0, labels.Length - 1);

            EditorGUILayout.LabelField("Role (route start / finish)", EditorStyles.boldLabel);
            int next = GUILayout.Toolbar(current, labels);
            if (next != current)
            {
                Undo.RecordObject(hold, "Change hold role");
                hold.role = (RouteRole)next;
                EditorUtility.SetDirty(hold);
            }

            if (hold.role != RouteRole.Normal)
            {
                EditorGUILayout.Space(2);
                var so = new SerializedObject(hold);
                EditorGUILayout.PropertyField(so.FindProperty("markerPrefab"));
                EditorGUILayout.PropertyField(so.FindProperty("markerOffset"));
                so.ApplyModifiedProperties();

                string fallback = hold.config != null
                    ? (hold.role == RouteRole.Start
                        ? (hold.config.holdStartMarkerPrefab != null ? hold.config.holdStartMarkerPrefab.name : "(none)")
                        : (hold.config.holdFinishMarkerPrefab != null ? hold.config.holdFinishMarkerPrefab.name : "(none)"))
                    : "(no config)";
                EditorGUILayout.HelpBox(
                    "Marker spawns on this hold at runtime when the game plays (Start/Finish holds also drive the route timer).\n" +
                    "Per-hold prefab above; falls back to the config's " +
                    (hold.role == RouteRole.Start ? "Start" : "Finish") + " marker: " + fallback,
                    MessageType.None);
            }
        }

        static void AutoFillConfig(LevelstepHold hold)
        {
            if (hold.config != null) return;
            var cfg = AssetDatabase.LoadAssetAtPath<LevelstepClimbingConfig>(LevelstepClimbingConfig.DefaultConfigPath);
            if (cfg == null) return;
            hold.config = cfg;
            EditorUtility.SetDirty(hold);
        }

        static void DrawRoute(LevelstepHold hold)
        {
            var tag = hold.GetComponent<HoldRouteTag>();
            if (tag == null)
            {
                tag = Undo.AddComponent<HoldRouteTag>(hold.gameObject);
                EditorUtility.SetDirty(hold);
            }

            string[] labels = { "Easy", "Moderate", "Hard" };
            int current = Mathf.Clamp((int)tag.routeType, 0, labels.Length - 1);

            EditorGUILayout.LabelField("Route (difficulty)", EditorStyles.boldLabel);
            int next = GUILayout.Toolbar(current, labels);
            if (next != current)
            {
                Undo.RecordObject(tag, "Change hold route");
                tag.routeType = (RouteType)next;
                EditorUtility.SetDirty(tag);
            }
        }
    }
}