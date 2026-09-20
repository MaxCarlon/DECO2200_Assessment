using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Levelstep.Editor
{
    /// <summary>
    /// Keeps the test scene in sync with the manual workflow: removes any legacy one-shot wall
    /// ("Climbing Wall" built by the old Rebuild All tool) and drops in a LevelstepWall prefab
    /// instance with the configured wall model pre-assigned (which appears instantly thanks to
    /// auto-apply). Holds are NOT generated - place them by hand with the Snap Hold tool.
    /// </summary>
    public static class LevelstepSceneTools
    {
        const string WallPrefabPath = "Assets/Levelstep/Prefabs/LevelstepWall.prefab";

        [MenuItem("Tools/Levelstep/Place Preview Wall in Scene")]
        public static void PlacePreviewWallInScene()
        {
            var cfg = AssetDatabase.LoadAssetAtPath<LevelstepClimbingConfig>(LevelstepClimbingConfig.DefaultConfigPath);
            if (cfg == null)
            {
                Debug.LogWarning("[Levelstep] No ClimbingConfig asset - nothing to place against.");
                return;
            }

            var wallAsset = AssetDatabase.LoadAssetAtPath<GameObject>(WallPrefabPath);
            if (wallAsset == null)
            {
                LevelstepPlacementTools.CreateAuthoringPrefabs();
                wallAsset = AssetDatabase.LoadAssetAtPath<GameObject>(WallPrefabPath);
                if (wallAsset == null)
                {
                    Debug.LogError("[Levelstep] Could not load or create " + WallPrefabPath);
                    return;
                }
            }

            var scene = EditorSceneManager.OpenScene(cfg.scenePath, OpenSceneMode.Single);

            foreach (var root in new List<GameObject>(scene.GetRootGameObjects()))
            {
                if (root == null) continue;
                if (root.name.StartsWith("Climbing Wall"))
                    Object.DestroyImmediate(root);
            }

            var wall = (GameObject)PrefabUtility.InstantiatePrefab(wallAsset);
            wall.name = "Climbing Wall";
            wall.transform.SetPositionAndRotation(cfg.wallPosition, Quaternion.Euler(0f, cfg.wallYaw, 0f));

            var component = wall.GetComponent<LevelstepWall>();
            Debug.Assert(component != null, "LevelstepWall prefab missing its LevelstepWall component.");

            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(cfg.wallModelPath);
            if (modelAsset != null)
            {
                component.model = modelAsset;
                ModelTools.RebuildWall(component);
            }
            else
            {
                Debug.LogWarning("[Levelstep] No wall model found at '" + cfg.wallModelPath +
                                 "'. Assign an FBX to the LevelstepWall's Model slot and it will appear automatically.");
            }

            EditorSceneManager.SaveScene(scene, cfg.scenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[Levelstep] Preview wall placed in '" + cfg.scenePath + "'. Add holds with Tools/Levelstep/Snap Hold.");
        }
    }
}