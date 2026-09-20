using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Climbing;

namespace Levelstep.Editor
{
    /// <summary>
    /// Manual authoring tools for the prefab workflow:
    ///   - Snap Hold (toggle): click a wall face in the Scene view to drop a LevelstepHold prefab,
    ///     aligned to the wall's surface normal and parented under the wall's "Holds" child.
    ///   - Ensure Authoring Prefabs: recreates LevelstepHold / LevelstepWall prefabs if missing.
    /// Route assignment is fully manual: select a placed hold and set its Route (Easy / Moderate /
    /// Hard) in the inspector. The model slots auto-apply, so there is no separate rebuild step.
    /// </summary>
    public static class LevelstepPlacementTools
    {
        const string SnapMenu = "Tools/Levelstep/Snap Hold (click wall to add)";
        const string HoldPrefabPath = "Assets/Levelstep/Prefabs/LevelstepHold.prefab";

        static bool s_SnapMode;

        public static GameObject DefaultHoldPrefab => AssetDatabase.LoadAssetAtPath<GameObject>(HoldPrefabPath);

        // -----------------------------------------------------------------------
        // Authoring prefab creation
        // -----------------------------------------------------------------------

        [MenuItem("Tools/Levelstep/Ensure Authoring Prefabs (Hold / Wall)")]
        public static void EnsureAuthoringPrefabsMenu()
        {
            CreateAuthoringPrefabs();
        }

        public static void CreateAuthoringPrefabs()
        {
            const string folder = "Assets/Levelstep/Prefabs";

            string holdPath = folder + "/LevelstepHold.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(holdPath) == null)
            {
                var go = new GameObject("LevelstepHold", typeof(Rigidbody), typeof(LevelstepHold), typeof(ClimbInteractable), typeof(HoldRouteTag));
                var rb = go.GetComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
                PrefabUtility.SaveAsPrefabAsset(go, holdPath);
                Object.DestroyImmediate(go);
            }

            string wallPath = folder + "/LevelstepWall.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(wallPath) == null)
            {
                var go = new GameObject("LevelstepWall", typeof(LevelstepWall));
                PrefabUtility.SaveAsPrefabAsset(go, wallPath);
                Object.DestroyImmediate(go);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[Levelstep] Authoring prefabs ready: LevelstepHold, LevelstepWall.");
        }

        // -----------------------------------------------------------------------
        // Snap hold (click a wall face)
        // -----------------------------------------------------------------------

        [MenuItem(SnapMenu)]
        public static void ToggleSnapMode()
        {
            s_SnapMode = !s_SnapMode;
            if (s_SnapMode)
            {
                SceneView.duringSceneGui += OnSceneGUI;
                Debug.Log("[Levelstep] Snap Hold ON - click a wall face in the Scene view to drop a hold aligned to its normal. Click the menu again to disable.");
            }
            else
            {
                SceneView.duringSceneGui -= OnSceneGUI;
                Debug.Log("[Levelstep] Snap Hold OFF.");
            }
            SceneView.RepaintAll();
        }

        [MenuItem(SnapMenu, true)]
        public static bool ToggleSnapModeEnabled()
        {
            Menu.SetChecked(SnapMenu, s_SnapMode);
            return true;
        }

        static void OnSceneGUI(SceneView sceneView)
        {
            var current = Event.current;
            if (current == null) return;

            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(controlId);

            Handles.BeginGUI();
            GUILayout.BeginArea(new Rect(10, 10, 360, 64));
            GUILayout.Label("Snap Hold: click a wall face (Esc to cancel).");
            GUILayout.EndArea();
            Handles.EndGUI();

            if (current.type == EventType.MouseDown && current.button == 0 &&
                !current.alt && !current.control && !current.shift)
            {
                var ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
                if (ModelTools.FindSurfaceAlong(ray, 1000f, out RaycastHit hit))
                {
                    PlaceOneAt(hit);
                    current.Use();
                }
            }

            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
            {
                ToggleSnapMode();
                current.Use();
            }
        }

        static void PlaceOneAt(RaycastHit hit)
        {
            var holdAsset = DefaultHoldPrefab;
            if (holdAsset == null)
            {
                Debug.LogWarning("[Levelstep] Missing hold prefab at " + HoldPrefabPath +
                                 " - run Tools/Levelstep/Ensure Authoring Prefabs first.");
                return;
            }

            var hold = (GameObject)PrefabUtility.InstantiatePrefab(holdAsset);

            var wall = hit.collider.GetComponentInParent<LevelstepWall>();
            if (wall != null)
                hold.transform.SetParent(ModelTools.EnsureHoldsChild(wall.transform), true);

            var holdComponent = hold.GetComponent<LevelstepHold>();
            ModelTools.PositionOnSurface(holdComponent, hit);
            ModelTools.ApplyHoldMaterial(hold.transform);

            if (hold.GetComponent<HoldRouteTag>() == null)
                hold.AddComponent<HoldRouteTag>();

            EditorUtility.SetDirty(hold);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = hold;
            Debug.Log("[Levelstep] Hold placed on '" + (wall != null ? wall.name : "scene root") +
                      "'. Select it to set its Route (Easy / Moderate / Hard).");
        }
    }
}