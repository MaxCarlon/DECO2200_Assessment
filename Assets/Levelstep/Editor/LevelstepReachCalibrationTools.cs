using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Levelstep.Editor
{
    /// <summary>Additive reach setup reusing the existing calibration UI and sequence, not a second UI framework.</summary>
    public static class LevelstepReachCalibrationTools
    {
        public const string PrefabPath = "Assets/Levelstep/Prefabs/SimulatedReachCalibration.prefab";

        [MenuItem("Tools/Levelstep/Calibration/Add Simulated Reach Calibration")]
        public static void AddToActiveScene()
        {
            if (Application.isPlaying) return;
            var scene = SceneManager.GetActiveScene();
            SimulatedCalibrationSequence balance = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var sequence in root.GetComponentsInChildren<SimulatedCalibrationSequence>(true))
                {
                    if (sequence.GetComponent<ReachCalibrationSettings>() != null)
                    {
                        Selection.activeGameObject = sequence.gameObject;
                        return;
                    }
                    balance = sequence;
                }
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(EnsurePrefab(), scene);
            Undo.RegisterCreatedObjectUndo(instance, "Add simulated reach calibration");
            instance.name = "Simulated Reach Calibration";
            if (balance != null)
            {
                // Initial placement only; subsequent layout authoring is independent.
                instance.transform.SetParent(balance.transform.parent, false);
                instance.transform.localPosition = balance.transform.localPosition;
                instance.transform.localRotation = balance.transform.localRotation;
                instance.transform.localScale = balance.transform.localScale;
            }
            else if (Camera.main != null)
            {
                instance.transform.SetParent(Camera.main.transform, false);
                instance.transform.localPosition = new Vector3(0f, -0.05f, 1.2f);
            }
            else instance.transform.position = new Vector3(0f, 1.6f, 1.2f);
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            Selection.activeGameObject = instance;
            EditorSceneManager.MarkSceneDirty(scene);
        }

        public static void IntegrateClimbingScene()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Levelstep/Scenes/ClimbingScene.unity");
            AddToActiveScene();
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[ReachCalibrationSetup] Two-stage simulated reach calibration integrated; right secondary/B activation, no measurement or grappling.");
        }

        public static GameObject EnsurePrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null) return existing;
            LevelstepCalibrationTools.EnsurePrefab();
            // Copy the authored UI intact into an independent prefab. Its functionality remains shared C# code.
            var root = PrefabUtility.LoadPrefabContents(LevelstepCalibrationTools.PrefabPath);
            try
            {
                root.name = "Simulated Reach Calibration";
                var sequence = root.GetComponent<SimulatedCalibrationSequence>();
                sequence.activationInput.action?.Dispose();
                sequence.activationInput = new InputActionProperty(new InputAction("Simulated Reach Calibration", InputActionType.Button,
                    "<XRController>{RightHand}/secondaryButton"));
                sequence.calibrationDuration = 3f;
                sequence.instructionText = "Extend your arm horizontally as far as comfortably possible.";
                sequence.additionalStages = new[]
                {
                    new SimulatedCalibrationStage
                    {
                        instructionText = "Raise your arm vertically as high as comfortably possible.",
                        duration = 3f
                    }
                };
                sequence.completionMessageDuration = 2f;
                sequence.completionText = "Reach Calibration Complete";
                sequence.initiallyVisible = false;
                root.AddComponent<ReachCalibrationSettings>();
                var panel = sequence.panel;
                panel.title = "REACH CALIBRATION";
                panel.prototypeLabel = "";
                panel.ShowReady(sequence.instructionText);
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
