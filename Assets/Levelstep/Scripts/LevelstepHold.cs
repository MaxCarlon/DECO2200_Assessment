using UnityEngine;

namespace Levelstep
{
    public enum RouteRole
    {
        Normal = 0,
        Start = 1,
        Finish = 2
    }

    /// <summary>
    /// Rides on the root of a climb-ready hold. Assign a model (FBX/prefab) to <see cref="model"/>
    /// and the mesh is applied automatically - colliders, shared hold material and culling flags
    /// are baked in, in the scene or in Prefab Mode. Set the difficulty on the neighbouring
    /// <see cref="HoldRouteTag"/> per hold, then either snap it to a wall face with the inspector
    /// button or click a wall face in the Scene view (Tools/Levelstep/Snap Hold).
    ///
    /// Mark a hold as Start or Finish to define the route; a marker prefab (e.g. a green or
    /// checkered flag) is spawned on it at runtime so players can tell where a route begins and
    /// ends. The route timer starts when the Start hold is grabbed and stops on the Finish hold.
    /// </summary>
    public class LevelstepHold : MonoBehaviour
    {
        [Tooltip("Role of this hold on the route. Start/Finish spawn a marker so players can tell where the route begins and ends (the route timer also watches these).")]
        public RouteRole role = RouteRole.Normal;

        [Tooltip("Marker prefab (e.g. a small green or checkered flag) spawned on this hold when it is a Start/Finish hold. Leave empty to use the config's Start/Finish marker prefabs.")]
        public GameObject markerPrefab;

        [Tooltip("Local position the spawned marker sits at, relative to the hold root (defaults to floating above the hold).")]
        public Vector3 markerOffset = new Vector3(0f, 0.5f, 0f);

        [Tooltip("Config asset used for the Start/Finish marker prefab fallbacks. Auto-assigned in the inspector.")]
        public LevelstepClimbingConfig config;

        [Tooltip("Hold model (FBX or prefab). Assigning it applies the mesh to this hold automatically.")]
        public GameObject model;

        [Tooltip("Add convex MeshColliders to the hold model when applied.")]
        public bool generateColliders = true;

        [Tooltip("How far the hold floats off the wall face when snapped or placed (metres).")]
        public float surfaceOffset = 0.08f;

        [Tooltip("Extra orientation applied after aligning to the wall surface normal (degrees).")]
        public float pitchOffset = 0f;
        public float yawOffset = 0f;
        public float rollOffset = 0f;

        [SerializeField, HideInInspector] GameObject m_AppliedModel;
        [SerializeField, HideInInspector] GameObject m_PlacedMarker;

        void Start()
        {
            if (role == RouteRole.Normal || m_PlacedMarker != null) return;

            GameObject prefab = markerPrefab;
            if (prefab == null && config != null)
            {
                prefab = role == RouteRole.Start ? config.holdStartMarkerPrefab
                    : role == RouteRole.Finish ? config.holdFinishMarkerPrefab
                    : null;
            }
            if (prefab == null) return;

            var marker = Instantiate(prefab, transform);
            marker.transform.localPosition = markerOffset;
            marker.transform.localRotation = Quaternion.identity;
            marker.name = role == RouteRole.Start ? "StartMarker" : "FinishMarker";
            m_PlacedMarker = marker;
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (Application.isPlaying) return;
            if (model == m_AppliedModel) return;
            m_AppliedModel = model;
            ModelTools.RebuildHold(this);
        }

        void OnDrawGizmos()
        {
            if (role == RouteRole.Normal) return;

            Vector3 pos = transform.TransformPoint(markerOffset);
            Gizmos.color = role == RouteRole.Start ? new Color(0.2f, 1f, 0.3f, 0.9f) : new Color(0.9f, 0.9f, 0.9f, 0.9f);
            Gizmos.DrawSphere(pos, 0.06f);
            Gizmos.DrawWireSphere(pos, 0.06f);

            string label = role == RouteRole.Start ? "START" : "FINISH";
            UnityEditor.Handles.Label(pos + Vector3.up * 0.14f, label);
        }
#endif
    }
}