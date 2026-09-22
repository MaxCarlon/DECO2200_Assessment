using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Climbing;

namespace Levelstep
{
    public class DifficultyRouteController : MonoBehaviour
    {
        public static DifficultyRouteController Instance { get; private set; }

        public LevelstepClimbingConfig config;

        public static event Action<int> SelectionChanged;

        sealed class Entry
        {
            public int routeIndex;
            public ClimbInteractable interactable;
            public readonly List<Renderer> renderers = new List<Renderer>();
            public readonly List<MaterialPropertyBlock> blocks = new List<MaterialPropertyBlock>();
        }

        Entry[] m_Entries;
        int m_Selected;
        Color m_WallTone = Color.gray;

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            Collect();
            int selected = config != null ? config.defaultDifficultyIndex : 1;
            var ctrl = ClimbStaminaController.Instance;
            if (ctrl != null) selected = ctrl.DifficultyIndex;
            if (config != null && config.rememberDifficulty &&
                PlayerPrefs.HasKey(ClimbStaminaController.DifficultyPlayerPrefsKey))
                selected = PlayerPrefs.GetInt(ClimbStaminaController.DifficultyPlayerPrefsKey, selected);
            SetSelected(selected);
        }

        public int SelectedIndex => m_Selected;

        void Collect()
        {
            var tags = UnityEngine.Object.FindObjectsByType<HoldRouteTag>(FindObjectsInactive.Include);
            m_Entries = new Entry[tags.Length];
            SampleWallTone(tags);
            for (int i = 0; i < tags.Length; i++)
            {
                var entry = new Entry { routeIndex = (int)tags[i].routeType };
                entry.interactable = tags[i].GetComponentInChildren<ClimbInteractable>(true);
                foreach (var r in tags[i].GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    entry.renderers.Add(r);
                    entry.blocks.Add(new MaterialPropertyBlock());
                }
                m_Entries[i] = entry;
            }
            Debug.Log("[Levelstep] Route controller found " + m_Entries.Length + " tagged holds. Wall tone " + m_WallTone + ". " +
                      (m_Entries.Length > 0 && m_Entries[0].renderers.Count > 0
                          ? "Sample renderer bounds: " + m_Entries[0].renderers[0].bounds
                          : "No renderers found."));
        }

        // Walk up from a hold to the nearest ancestor that actually renders (the wall) and sample
        // its lit albedo so inactive holds fade toward what the wall looks like lit.
        void SampleWallTone(HoldRouteTag[] tags)
        {
            if (tags == null || tags.Length == 0) return;

            Renderer wallRenderer = null;
            for (Transform t = tags[0].transform.parent; t != null && wallRenderer == null; t = t.parent)
            {
                var r = t.GetComponent<Renderer>();
                if (r != null && r.sharedMaterial != null) wallRenderer = r;
            }
            if (wallRenderer == null) return;

            var mat = wallRenderer.sharedMaterial;
            Color baseColor = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor")
                : mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.gray;

            float lightFactor = 1f;
            var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i].type != LightType.Directional) continue;
                lightFactor = Mathf.Max(0.2f, lights[i].intensity);
                break;
            }

            // Floor the tone so inactive holds can never read as black, even on a dark wall -
            // they stay "ghosted" (light grey) instead of vanishing into blackness.
            const float minTone = 0.4f;
            m_WallTone = new Color(
                Mathf.Max(minTone, Mathf.Clamp01(baseColor.r * lightFactor)),
                Mathf.Max(minTone, Mathf.Clamp01(baseColor.g * lightFactor)),
                Mathf.Max(minTone, Mathf.Clamp01(baseColor.b * lightFactor)),
                1f);
        }

        public void SetSelected(int index)
        {
            m_Selected = index;
            SelectionChanged?.Invoke(index);
            Apply();
        }

        void Apply()
        {
            if (config == null || config.difficultyPresets == null || config.difficultyPresets.Count == 0) return;
            if (m_Entries == null) return;

            int presetCount = config.difficultyPresets.Count;
            float faded = Mathf.Clamp01(config.fadedOpacity);

            for (int i = 0; i < m_Entries.Length; i++)
            {
                var entry = m_Entries[i];
                bool selectedRoute = entry.routeIndex == m_Selected;
                if (entry.interactable != null)
                    entry.interactable.enabled = selectedRoute;

                // Opaque fade: non-selected holds blend toward the lit wall colour so they read as
                // "faded into the wall" ghosts - the translucency illusion - with no transparent pass.
                Color color = config.difficultyPresets[entry.routeIndex % presetCount].routeColor;
                float t = selectedRoute ? 0f : Mathf.Clamp01(1f - faded);
                color = Color.Lerp(color, m_WallTone, t);
                color.a = 1f;

                for (int r = 0; r < entry.renderers.Count; r++)
                {
                    var renderer = entry.renderers[r];
                    if (renderer == null) continue;
                    ApplyBlock(renderer, entry.blocks[r], color);
                }
            }
        }

        static void ApplyBlock(Renderer renderer, MaterialPropertyBlock block, Color color)
        {
            block.Clear();
            if (renderer.sharedMaterial != null)
            {
                if (renderer.sharedMaterial.HasProperty("_BaseColor"))
                    block.SetColor("_BaseColor", color);
                else if (renderer.sharedMaterial.HasProperty("_Color"))
                    block.SetColor("_Color", color);
            }
            renderer.SetPropertyBlock(block);
        }
    }
}