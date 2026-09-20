using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Levelstep.Editor
{
    /// <summary>
    /// Adds (or updates) the "Balance Board" GameObject in the active scene with the sensor bridge,
    /// the balance meter controller and the headset-view HUD, all wired to the ClimbingConfig asset.
    /// </summary>
    public static class LevelstepBalanceTools
    {
        [MenuItem("Tools/Levelstep/Add Balance Board to Scene")]
        public static void AddBalanceBoardToScene()
        {
            var config = AssetDatabase.LoadAssetAtPath<LevelstepClimbingConfig>(LevelstepClimbingConfig.DefaultConfigPath);

            var scenePath = config != null && !string.IsNullOrEmpty(config.scenePath)
                ? config.scenePath
                : "Assets/Levelstep/Scenes/ClimbingScene.unity";
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Debug.Log("[Levelstep] Balance board tool opened scene '" + scene.path + "'.");

            var root = Object.FindFirstObjectByType<BalanceBoardBridge>();
            if (root == null)
            {
                var go = new GameObject("Balance Board");
                root = go.AddComponent<BalanceBoardBridge>();
                go.AddComponent<BalanceMeterController>();
                go.AddComponent<BalanceBoardUI>();
                Debug.Log("[Levelstep] Created 'Balance Board' object.");
            }

            if (config != null)
            {
                foreach (var component in root.GetComponents<Component>())
                {
                    if (component == null) continue;
                    var field = component.GetType().GetField("config", BindingFlags.Public | BindingFlags.Instance);
                    if (field != null) field.SetValue(component, config);
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Levelstep] Balance Board wired in '{scene.path}'. Play, press C to connect, B to calibrate flat.");
        }
    }
}