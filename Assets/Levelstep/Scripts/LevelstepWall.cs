using UnityEngine;

namespace Levelstep
{
    /// <summary>
    /// Rides on the root of a climbing wall prefab. Assign a model (FBX/prefab) to <see cref="model"/>
    /// and the mesh is applied automatically (with non-convex MeshColliders). It only supplies the
    /// surfaces that holds snap onto - drop LevelstepHold prefabs on it with the Snap Hold tool and
    /// set each hold's difficulty per hold.
    /// </summary>
    public class LevelstepWall : MonoBehaviour
    {
        [Tooltip("Wall model (FBX or prefab). Assigning it applies the mesh to this wall automatically.")]
        public GameObject model;

        [Tooltip("Add non-convex MeshColliders to the model when applied.")]
        public bool generateColliders = true;

        [SerializeField, HideInInspector] GameObject m_AppliedModel;

#if UNITY_EDITOR
        void OnValidate()
        {
            if (Application.isPlaying) return;
            if (model == m_AppliedModel) return;
            m_AppliedModel = model;
            ModelTools.RebuildWall(this);
        }
#endif
    }
}