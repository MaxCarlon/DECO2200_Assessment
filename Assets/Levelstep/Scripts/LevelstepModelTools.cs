using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Climbing;
#endif

namespace Levelstep
{
    /// <summary>
    /// Editor-only model swapping / snapping shared by the hold and wall prefabs (auto-apply in
    /// <see cref="LevelstepHold.OnValidate"/> / <see cref="LevelstepWall.OnValidate"/>) and the
    /// editor tools. Lives in the runtime namespace so MonoBehaviours can call it; every member
    /// is guarded by #if UNITY_EDITOR.
    /// </summary>
    public static class ModelTools
    {
        public const string HoldMaterialPath = "Assets/Levelstep/Materials/LevelstepHold.mat";

#if UNITY_EDITOR
        static bool s_Guard;

        public static bool IsRebuilding => s_Guard;

        // -----------------------------------------------------------------------
        // Model application (auto-apply path used by OnValidate and the editor)
        // -----------------------------------------------------------------------

        public static void RebuildHold(LevelstepHold hold)
        {
            if (s_Guard || hold == null) return;
            if (Application.isPlaying) return;

            s_Guard = true;
            try
            {
                GameObject modelGo = null;
                if (hold.model != null)
                    modelGo = ReplaceModel(hold.transform, hold.model);

                if (hold.generateColliders && modelGo != null)
                    AddMeshColliders(modelGo, convex: false);

                DisableCulling(hold.transform);
                ApplyHoldMaterial(hold.transform);
                EditorUtility.SetDirty(hold);
            }
            finally
            {
                s_Guard = false;
            }
        }

        public static void RebuildWall(LevelstepWall wall)
        {
            if (s_Guard || wall == null) return;
            if (Application.isPlaying) return;

            s_Guard = true;
            try
            {
                GameObject modelGo = null;
                if (wall.model != null)
                    modelGo = ReplaceModel(wall.transform, wall.model);

                if (wall.generateColliders && modelGo != null)
                    AddMeshColliders(modelGo, convex: false);

                EditorUtility.SetDirty(wall);
            }
            finally
            {
                s_Guard = false;
            }
        }

        /// <summary>Swaps the "Model" child of <paramref name="root"/> for <paramref name="modelAsset"/>.</summary>
        static GameObject ReplaceModel(Transform root, GameObject modelAsset)
        {
            DestroyModelChildren(root);
            if (modelAsset == null) return null;

            var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
            model.name = "Model";
            model.transform.SetParent(root, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            return model;
        }

        public static void DestroyModelChildren(Transform root)
        {
            if (root == null) return;
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                if (root.GetChild(i).name == "Model")
                    UnityEngine.Object.DestroyImmediate(root.GetChild(i).gameObject);
            }
        }

        // -----------------------------------------------------------------------
        // Colliders, culling, material
        // -----------------------------------------------------------------------

        public static void AddMeshColliders(GameObject root, bool convex)
        {
            if (root == null) return;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null || mf.sharedMesh == null) continue;
                var col = mf.GetComponent<MeshCollider>();
                if (col == null) col = mf.gameObject.AddComponent<MeshCollider>();
                col.sharedMesh = mf.sharedMesh;
                col.convex = convex;
            }
        }

        /// <summary>Disables small-mesh culling + dynamic-occludee on a whole hierarchy (held mesh stability in VR).</summary>
        public static void DisableCulling(Transform root)
        {
            if (root == null) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                var so = new SerializedObject(r);
                var smallMeshCulling = so.FindProperty("m_SmallMeshCulling");
                if (smallMeshCulling != null) smallMeshCulling.boolValue = false;
                var dynamicOccludee = so.FindProperty("m_DynamicOccludee");
                if (dynamicOccludee != null) dynamicOccludee.boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        public static Bounds ComputeBounds(GameObject root)
        {
            Bounds bounds = new Bounds();
            bool initialized = false;
            if (root == null) return bounds;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is MeshRenderer || r is SkinnedMeshRenderer)
                {
                    if (!initialized) { bounds = r.bounds; initialized = true; }
                    else bounds.Encapsulate(r.bounds);
                }
            }
            return bounds;
        }

        /// <summary>
        /// Shared opaque hold material. The hold tooling and runtime dimming point at this one
        /// asset so route fading via MaterialPropertyBlock stays consistent and VR-stable.
        /// </summary>
        public static Material GetOrCreateHoldMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(HoldMaterialPath);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                if (shader == null)
                {
                    Debug.LogWarning("[Levelstep] No Lit shader available for the hold material.");
                    return null;
                }

                var dir = Path.GetDirectoryName(HoldMaterialPath)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(dir)) EnsureFolder(dir);
                mat = new Material(shader);
                mat.name = "LevelstepHold";
                AssetDatabase.CreateAsset(mat, HoldMaterialPath);
            }

            // Force a stable OPAQUE hold surface: writes depth, Geometry queue, no alpha pass.
            // Truly transparent holds were confirmed to pop in/out under VR stereo rendering, so
            // the route blend is expressed by fading the colour toward the wall tone instead.
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_BLENDMODE_ALPHA");
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.One);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 1f);
            mat.SetOverrideTag("RenderType", "Opaque");
            mat.renderQueue = (int)RenderQueue.Geometry;
            Color white = Color.white;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", white);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", white);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }

        public static void ApplyHoldMaterial(Transform holdRoot)
        {
            var holdMat = GetOrCreateHoldMaterial();
            if (holdMat == null || holdRoot == null) return;
            foreach (var r in holdRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (r != null) r.sharedMaterial = holdMat;
            }
        }

        static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            path = path.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            string leaf = Path.GetFileName(path);
            if (string.IsNullOrEmpty(leaf)) return;
            AssetDatabase.CreateFolder(parent, leaf);
        }

        // -----------------------------------------------------------------------
        // Wall-face placement / normal matching
        // -----------------------------------------------------------------------

        /// <summary>Aligns a hold to a wall face: offset off the surface and oriented along its normal (+Z points out of the wall).</summary>
        public static void PositionOnSurface(LevelstepHold hold, RaycastHit hit)
        {
            hold.transform.position = hit.point + hit.normal * Mathf.Max(0f, hold.surfaceOffset);

            Vector3 fwd = hit.normal;
            Vector3 up = Mathf.Abs(Vector3.Dot(fwd, Vector3.up)) > 0.9f ? hold.transform.right : Vector3.up;
            hold.transform.rotation = Quaternion.LookRotation(fwd, up) *
                                      Quaternion.Euler(hold.pitchOffset, hold.yawOffset, hold.rollOffset);
        }

        public static bool IsPlacableSurface(RaycastHit hit)
        {
            if (hit.collider == null) return false;
            if (hit.collider.GetComponentInParent<ClimbInteractable>() != null) return false; // never snap onto another hold
            return true;
        }

        /// <summary>
        /// Finds a surface to place a hold on along (or near) a view ray. Some wall meshes have open
        /// or recessed fronts, so if the straight ray misses we search with a thick sphere and then a
        /// small fan of tilted rays, keeping the hit nearest to the view line.
        /// </summary>
        public static bool FindSurfaceAlong(Ray ray, float maxDistance, out RaycastHit hit, float fallbackSpread = 0.5f)
        {
            if (Physics.Raycast(ray, out hit, maxDistance) && IsPlacableSurface(hit)) return true;

            if (Physics.SphereCast(ray, fallbackSpread, out RaycastHit sHit, maxDistance) && IsPlacableSurface(sHit))
            {
                hit = sHit;
                return true;
            }

            RaycastHit best = default;
            float bestDistance = float.MaxValue;
            float[] yaws = { -7f, -3.5f, 0f, 3.5f, 7f };
            float[] pitches = { -4f, -2f, 0f, 2f, 4f };
            foreach (var yaw in yaws)
            {
                foreach (var pitch in pitches)
                {
                    if (yaw == 0f && pitch == 0f) continue;
                    Vector3 dir = Quaternion.Euler(pitch, yaw, 0f) * ray.direction;
                    if (!Physics.Raycast(ray.origin, dir, out RaycastHit h, maxDistance)) continue;
                    if (!IsPlacableSurface(h)) continue;

                    Vector3 rel = h.point - ray.origin;
                    float t = Vector3.Dot(rel, ray.direction);
                    float distanceFromViewLine = (rel - ray.direction * t).magnitude;
                    if (distanceFromViewLine < bestDistance)
                    {
                        bestDistance = distanceFromViewLine;
                        best = h;
                    }
                }
            }
            if (best.collider == null)
            {
                // Last resort for open, finned or recessed wall faces: search all world axes from
                // points along the view line and keep the surface closest to the view direction.
                float closestT = float.MaxValue;
                for (float t = 2f; t <= 60f && best.collider == null; t += 4f)
                {
                    Vector3 center = ray.origin + ray.direction * t;
                    if (FindSurfaceNear(center, 4f, out RaycastHit near))
                    {
                        float tt = Vector3.Dot(near.point - ray.origin, ray.direction);
                        if (tt < closestT) { closestT = tt; best = near; }
                    }
                }
            }
            if (best.collider == null) return false;
            hit = best;
            return true;
        }

        /// <summary>
        /// Searches the world axes around <paramref name="center"/> for the nearest placable surface.
        /// Handy for wall meshes that only register hits from certain sides (open fronts, fins, overhangs).
        /// </summary>
        public static bool FindSurfaceNear(Vector3 center, float searchRadius, out RaycastHit hit)
        {
            var dirs = new[]
            {
                Vector3.forward, -Vector3.forward,
                Vector3.up, -Vector3.up,
                Vector3.right, -Vector3.right
            };

            RaycastHit best = default;
            float bestSqr = float.MaxValue;
            foreach (var d in dirs)
            {
                Vector3 origin = center + d * 0.25f;
                if (!Physics.Raycast(origin, d, out RaycastHit h, searchRadius + 0.25f)) continue;
                if (!IsPlacableSurface(h)) continue;
                float sqr = (h.point - center).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = h; }
            }
            if (best.collider == null)
            {
                hit = default;
                return false;
            }
            hit = best;
            return true;
        }

        /// <summary>
        /// Mounts a hold on the nearest placable surface around its current position, matching the
        /// surface normal. Searches all six local directions and keeps the nearest hit.
        /// </summary>
        public static bool SnapHoldToWall(LevelstepHold hold)
        {
            if (s_Guard || hold == null) return false;
            if (Application.isPlaying) return false;

            var dirs = new[]
            {
                -hold.transform.forward, hold.transform.forward,
                -hold.transform.up, hold.transform.up,
                -hold.transform.right, hold.transform.right
            };

            Physics.SyncTransforms();
            float bestSqr = float.MaxValue;
            RaycastHit best = default;
            foreach (var d in dirs)
            {
                Vector3 origin = hold.transform.position + d * 0.5f;
                if (!Physics.Raycast(origin, d, out RaycastHit hit, 50f)) continue;
                if (!IsPlacableSurface(hit)) continue;
                float sqr = (hit.point - hold.transform.position).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = hit; }
            }
            if (best.collider == null) return false;

            s_Guard = true;
            try
            {
                PositionOnSurface(hold, best);
                EditorUtility.SetDirty(hold);
            }
            finally
            {
                s_Guard = false;
            }
            return true;
        }

        public static Transform EnsureHoldsChild(Transform parent, string childName = "Holds")
        {
            var existing = parent != null ? parent.Find(childName) : null;
            if (existing != null) return existing;

            var go = new GameObject(childName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return go.transform;
        }
#endif
    }
}