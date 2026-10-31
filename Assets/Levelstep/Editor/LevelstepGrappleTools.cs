using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Climbing;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;
using Object = UnityEngine.Object;

namespace Levelstep.Editor
{
    public static class LevelstepGrappleTools
    {
        [MenuItem("Tools/Levelstep/Accessibility/Add or Upgrade Assisted Grappling")]
        public static void AddToActiveScene()
        {
            if (Application.isPlaying) return;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                var existing = root.GetComponentInChildren<AccessibilityGrappleController>(true);
                if (existing == null) continue;
                UpgradeAssistedGrappling(existing);
                Selection.activeGameObject = existing.gameObject;
                return;
            }
            var origin = Object.FindAnyObjectByType<XROrigin>();
            var manager = Object.FindAnyObjectByType<XRInteractionManager>();
            var settings = Object.FindAnyObjectByType<ReachCalibrationSettings>();
            if (origin == null || origin.Camera == null || manager == null || settings == null)
                throw new System.InvalidOperationException("Grappling setup requires the existing XR Origin, interaction manager and reach settings.");
            Transform left = null;
            foreach (var transform in origin.GetComponentsInChildren<Transform>(true))
                if (transform.name == "Left Controller") { left = transform; break; }
            if (left == null) throw new System.InvalidOperationException("Left Controller was not found on the current scene rig.");

            var go = new GameObject("Accessibility Grappling (Left X)");
            go.SetActive(false);
            go.transform.SetParent(origin.transform, false);
            Undo.RegisterCreatedObjectUndo(go, "Add accessibility grappling");
            var controller = go.AddComponent<AccessibilityGrappleController>();
            controller.physicalController = controller.aimTransform = left;
            controller.trackedHead = origin.Camera.transform;
            controller.trackingSpace = origin.CameraFloorOffsetObject.transform;
            controller.rigRoot = origin.transform;
            controller.interactionManager = manager;
            controller.stamina = Object.FindAnyObjectByType<ClimbStaminaController>();
            controller.reachSettings = settings;
            controller.reachCalibration = settings.GetComponent<SimulatedCalibrationSequence>();
            var proxy = new GameObject("Left Grapple Hand (XRI)").AddComponent<AccessibilityGrappleInteractor>();
            proxy.transform.SetParent(go.transform, false);
            proxy.controller = controller;
            proxy.interactionManager = manager;
            proxy.handedness = InteractorHandedness.Left;
            proxy.attachTransform = proxy.transform;
            controller.proxy = proxy;

            var line = new GameObject("Grapple Guide").AddComponent<LineRenderer>();
            line.transform.SetParent(go.transform, false);
            var sourceLine = left.GetComponentInChildren<LineRenderer>(true);
            if (sourceLine == null || sourceLine.sharedMaterial == null)
                throw new System.InvalidOperationException("Existing XRI line material was not found.");
            line.sharedMaterial = sourceLine.sharedMaterial;
            line.useWorldSpace = true; line.positionCount = 2; line.enabled = false;
            line.startWidth = line.endWidth = controller.guidingLineWidth;
            line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
            line.numCapVertices = 2;
            controller.guidingLine = line;

            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/Prefabs/Controllers/XR Controller Left.prefab");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, proxy.transform);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "Projected Controller (Virtual Hand)";
            // A renderer-only copy, not a second tracking/input/interaction hierarchy.
            foreach (var behaviour in model.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(behaviour);
            model.SetActive(false);
            controller.projectedHand = model.transform;
            var tracked = new List<Renderer>();
            foreach (var renderer in left.GetComponentsInChildren<Renderer>(true))
                if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer) tracked.Add(renderer);
            controller.trackedHandRenderers = tracked.ToArray();
            var links = new List<GrappleRendererLink>();
            foreach (var projected in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mesh = projected.GetComponent<MeshFilter>();
                if (mesh == null) continue;
                foreach (var candidate in tracked)
                {
                    var physicalMesh = candidate.GetComponent<MeshFilter>();
                    if (physicalMesh == null || physicalMesh.sharedMesh != mesh.sharedMesh) continue;
                    links.Add(new GrappleRendererLink { trackedRenderer = candidate, projectedRenderer = projected });
                    break;
                }
            }
            controller.rendererLinks = links.ToArray();
            UpgradeAssistedGrappling(controller);
            go.SetActive(true);
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = go;
        }

        static void UpgradeAssistedGrappling(AccessibilityGrappleController existing)
        {
            var owner = existing.modeController != null ? existing.modeController : existing;
            var origin = owner.GetComponentInParent<XROrigin>();
            if (origin == null) throw new System.InvalidOperationException("Existing grapple must be under XR Origin.");
            Undo.RecordObject(owner, "Upgrade assisted grappling");
            owner.physicalInteractor = owner.physicalController.GetComponentInChildren<NearFarInteractor>(true);
            var movement = owner.GetComponent<AccessibilityGrappleMovement>();
            if (movement == null) movement = Undo.AddComponent<AccessibilityGrappleMovement>(owner.gameObject);
            movement.mediator = origin.GetComponentInChildren<LocomotionMediator>(true);
            movement.climbProvider = origin.GetComponentInChildren<ClimbProvider>(true);
            movement.gravityProvider = origin.GetComponentInChildren<GravityProvider>(true);
            owner.movement = movement;
            owner.gameObject.name = "Accessibility Grappling (X Mode)";

            AccessibilityGrappleController right = null;
            foreach (var candidate in origin.GetComponentsInChildren<AccessibilityGrappleController>(true))
                if (candidate != owner && candidate.handSide == ClimbStaminaController.HandSide.Right) { right = candidate; break; }
            if (right == null)
            {
                Transform tracked = null;
                foreach (var child in origin.GetComponentsInChildren<Transform>(true))
                    if (child.name == "Right Controller") { tracked = child; break; }
                if (tracked == null) throw new System.InvalidOperationException("Right Controller was not found.");
                var go = new GameObject("Right Assisted Grapple");
                go.SetActive(false);
                go.transform.SetParent(owner.transform, false);
                Undo.RegisterCreatedObjectUndo(go, "Add right assisted grapple");
                right = go.AddComponent<AccessibilityGrappleController>();
                EditorUtility.CopySerialized(owner, right); // Preserve the existing authored tuning as the initial defaults.
                right.modeController = owner;
                right.handSide = ClimbStaminaController.HandSide.Right;
                right.physicalController = right.aimTransform = tracked;
                right.reachOriginOffset = new Vector3(Mathf.Abs(owner.reachOriginOffset.x), owner.reachOriginOffset.y, owner.reachOriginOffset.z);
                var proxy = new GameObject("Right Grapple Hand (XRI)").AddComponent<AccessibilityGrappleInteractor>();
                proxy.transform.SetParent(go.transform, false);
                proxy.controller = right; proxy.interactionManager = owner.interactionManager;
                proxy.handedness = InteractorHandedness.Right; proxy.attachTransform = proxy.transform;
                right.proxy = proxy;
                var line = Object.Instantiate(owner.guidingLine, go.transform);
                line.name = "Right Grapple Guide";
                right.guidingLine = line;
                var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/Prefabs/Controllers/XR Controller Right.prefab");
                var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, proxy.transform);
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                model.name = "Projected Right Controller (Virtual Hand)";
                foreach (var behaviour in model.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(behaviour);
                model.SetActive(false);
                right.projectedHand = model.transform;
                var renderers = new List<Renderer>();
                foreach (var renderer in tracked.GetComponentsInChildren<Renderer>(true))
                    if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer) renderers.Add(renderer);
                right.trackedHandRenderers = renderers.ToArray();
                var links = new List<GrappleRendererLink>();
                foreach (var projected in model.GetComponentsInChildren<MeshRenderer>(true))
                    foreach (var physical in renderers)
                    {
                        var source = physical.GetComponent<MeshFilter>();
                        if (source == null || source.sharedMesh != projected.GetComponent<MeshFilter>()?.sharedMesh) continue;
                        links.Add(new GrappleRendererLink { trackedRenderer = physical, projectedRenderer = projected });
                        break;
                    }
                right.rendererLinks = links.ToArray();
                go.SetActive(true);
            }
            Undo.RecordObject(right, "Wire assisted grappling");
            right.modeController = owner;
            right.movement = movement;
            right.physicalInteractor = right.physicalController.GetComponentInChildren<NearFarInteractor>(true);
            EditorUtility.SetDirty(owner); EditorUtility.SetDirty(right); EditorUtility.SetDirty(movement);
            EditorSceneManager.MarkSceneDirty(owner.gameObject.scene);
        }

        public static void IntegrateClimbingScene()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Levelstep/Scenes/ClimbingScene.unity");
            AddToActiveScene();
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[GrappleSetup] Shared X mode, both existing grip inputs and collision-aware assisted movement integrated.");
        }
    }
}
