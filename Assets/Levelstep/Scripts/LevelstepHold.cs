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
    /// and the mesh is applied automatically. Set the difficulty on the neighbouring
    /// <see cref="HoldRouteTag"/> per hold.
    ///
    /// Mark a hold as Start or Finish and assign <see cref="markerPrefab"/> (e.g. the LEVELstep
    /// START/FINISH text label prefabs). The marker is placed above the hold once and is only
    /// visible while that hold's route is the currently selected difficulty.
    /// </summary>
    public class LevelstepHold : MonoBehaviour
    {
        [Tooltip("Role of this hold on the route. Start/Finish show their markerPrefab text label above the hold while the matching difficulty is selected.")]
        public RouteRole role = RouteRole.Normal;

        [Tooltip("Prefab placed above this hold when marked Start or Finish (the LEVELstep text label prefabs).")]
        public GameObject markerPrefab;

        [Tooltip("Offset where the marker is placed, relative to the hold root (metres).")]
        public Vector3 markerOffset = new Vector3(0f, 0.5f, 0f);

        [Tooltip("Config asset used as a fallback for settings. Auto-assigned in the inspector.")]
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

        HoldRouteTag m_RouteTag;
        Transform m_Camera;

        void OnEnable()
        {
            DifficultyRouteController.SelectionChanged += OnSelectionChanged;
        }

        void OnDisable()
        {
            DifficultyRouteController.SelectionChanged -= OnSelectionChanged;
        }

        void Start()
        {
            if (role == RouteRole.Normal || m_PlacedMarker != null) return;
            if (markerPrefab == null)
            {
                Debug.LogWarning("[LevelstepHold] " + name + " is marked " + role + " but has no markerPrefab assigned.");
                return;
            }

            m_PlacedMarker = Instantiate(markerPrefab, transform);
            m_PlacedMarker.name = markerPrefab.name;
            m_PlacedMarker.transform.localPosition = markerOffset;
            m_PlacedMarker.transform.localRotation = Quaternion.identity;
            m_PlacedMarker.SetActive(false);

            RefreshMarker();
        }

        void OnSelectionChanged(int selected)
        {
            RefreshMarker();
        }

        void LateUpdate()
        {
            if (m_PlacedMarker == null || !m_PlacedMarker.activeSelf) return;
            if (m_Camera == null)
            {
                m_Camera = FindCamera();
                return;
            }

            Vector3 direction = m_PlacedMarker.transform.position - m_Camera.position;
            if (direction.sqrMagnitude < 0.0001f) return;
            m_PlacedMarker.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        }

        void RefreshMarker()
        {
            if (m_PlacedMarker == null) return;

            if (m_RouteTag == null) m_RouteTag = GetComponent<HoldRouteTag>();
            int route = m_RouteTag != null ? (int)m_RouteTag.routeType : -1;
            int selected = DifficultyRouteController.Instance != null
                ? DifficultyRouteController.Instance.SelectedIndex
                : -1;
            bool visible = route < 0 || selected < 0 || route == selected;

            if (m_PlacedMarker.activeSelf != visible) m_PlacedMarker.SetActive(visible);
        }

        static Transform FindCamera()
        {
            if (Camera.main != null) return Camera.main.transform;
            var cam = Object.FindAnyObjectByType<Camera>();
            return cam != null ? cam.transform : null;
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