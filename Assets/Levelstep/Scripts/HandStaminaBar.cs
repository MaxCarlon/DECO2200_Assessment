using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Levelstep
{
    public class HandStaminaBar : MonoBehaviour
    {
        static readonly Color DefaultFill = new Color(0.42f, 0.9f, 0.65f);
        static readonly Color DefaultBack = new Color(0f, 0f, 0f, 0.45f);
        static readonly Color DefaultHandTint = new Color(1f, 0.2f, 0.2f);

        ClimbStaminaController m_Controller;
        ClimbStaminaController.HandSide m_Side;
        LevelstepClimbingConfig m_Config;

        RectTransform m_FillRect;
        float m_BarWidth;
        float m_BarHeight;
        Transform m_Camera;
        bool m_HandleTint;
        int m_TintRetryFrame;
        Material[] m_TintMaterials;
        Color[] m_OriginalColors;
        float m_LastFillWidth = -1f;
        Color[] m_LastTintColors;
        int m_TintRetryCadence = 30;

        static Texture2D s_WhiteTexture;
        static Sprite s_WhiteSprite;

        public void Initialize(ClimbStaminaController controller, ClimbStaminaController.HandSide side, LevelstepClimbingConfig config)
        {
            m_Controller = controller;
            m_Side = side;
            m_Config = config;
            m_Camera = FindCamera();
            BuildUI();
            if (config != null && config.tintHands)
                SetupHandTint();
        }

        void LateUpdate()
        {
            if (m_Controller == null) return;
            UpdateFill();
            UpdateBillboard();
            if (!m_HandleTint && m_Config != null && m_Config.tintHands && Time.frameCount >= m_TintRetryFrame)
            {
                m_TintRetryFrame = Time.frameCount + m_TintRetryCadence;
                if (m_TintRetryCadence < 600) m_TintRetryCadence *= 2;
                SetupHandTint();
            }
            UpdateHandTint();
        }

        void BuildUI()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var rect = (RectTransform)transform;
            Vector2 size = m_Config != null ? m_Config.barSize : new Vector2(0.12f, 0.015f);
            rect.sizeDelta = size;
            rect.localPosition = m_Config != null ? m_Config.barOffset : new Vector3(0f, 0.08f, 0f);
            rect.localRotation = Quaternion.identity;

            m_BarWidth = size.x;
            m_BarHeight = size.y;

            CreateImage("Back", DefaultBack, rect);

            Color fill = m_Config != null ? m_Config.barFillColor : DefaultFill;
            var fillImg = CreateImage("Fill", fill, rect);
            var fillRect = (RectTransform)fillImg.transform;
            fillRect.anchorMin = new Vector2(0f, 0.5f);
            fillRect.anchorMax = new Vector2(0f, 0.5f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.anchoredPosition = Vector2.zero;
            fillRect.sizeDelta = new Vector2(m_BarWidth, m_BarHeight);
            m_FillRect = fillRect;
        }

        static Sprite WhiteSprite()
        {
            if (s_WhiteSprite != null) return s_WhiteSprite;
            if (s_WhiteTexture == null)
            {
                s_WhiteTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                s_WhiteTexture.SetPixel(0, 0, Color.white);
                s_WhiteTexture.Apply();
            }
            s_WhiteSprite = Sprite.Create(s_WhiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f));
            return s_WhiteSprite;
        }

        static Image CreateImage(string name, Color color, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var image = go.GetComponent<Image>();
            image.sprite = WhiteSprite();
            image.type = Image.Type.Simple;
            image.color = color;
            image.raycastTarget = false;

            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return image;
        }

        void UpdateFill()
        {
            float width = m_BarWidth * Mathf.Clamp01(m_Controller.GetNormalizedStamina(m_Side));
            if (Mathf.Abs(width - m_LastFillWidth) <= 0.0001f) return;
            m_LastFillWidth = width;
            m_FillRect.sizeDelta = new Vector2(width, m_BarHeight);
        }

        void UpdateBillboard()
        {
            if (m_Camera == null)
            {
                m_Camera = FindCamera();
                return;
            }

            Vector3 direction = transform.position - m_Camera.position;
            if (direction.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        }

        static Transform FindCamera()
        {
            if (Camera.main != null) return Camera.main.transform;
            var cam = Object.FindAnyObjectByType<Camera>();
            return cam != null ? cam.transform : null;
        }

        string SideKeyword()
        {
            string keyword = m_Side == ClimbStaminaController.HandSide.Left
                ? (m_Config != null ? m_Config.leftHandKeyword : "Left")
                : (m_Config != null ? m_Config.rightHandKeyword : "Right");
            return string.IsNullOrEmpty(keyword) ? (m_Side == ClimbStaminaController.HandSide.Left ? "Left" : "Right") : keyword;
        }

        void SetupHandTint()
        {
            if (transform.parent == null) return;

            Renderer[] renderers = transform.parent.GetComponentsInChildren<Renderer>(true);
            if (!HasTintableMaterial(renderers))
            {
                renderers = FindSideRenderers();
                if (renderers.Length == 0)
                {
                    m_HandleTint = false;
                    return;
                }
            }

            var mats = new Material[renderers.Length];
            var origins = new Color[renderers.Length];
            bool any = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                var owner = renderers[i];
                if (owner == null || owner.sharedMaterial == null) continue;
                if (!HasTintProperty(owner.sharedMaterial)) continue;
                mats[i] = owner.material;
                origins[i] = ReadColor(mats[i]);
                any = true;
            }

            m_TintMaterials = mats;
            m_OriginalColors = origins;
            m_HandleTint = any;
            if (any)
            {
                m_LastTintColors = new Color[mats.Length];
                for (int i = 0; i < m_LastTintColors.Length; i++)
                    m_LastTintColors[i] = new Color(-1f, -1f, -1f, -1f);
            }
        }

        Renderer[] FindSideRenderers()
        {
            string keyword = SideKeyword();
            var result = new List<Renderer>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
            {
                if (r == null) continue;
                if (AncestorMatches(r.transform, keyword, 4))
                    result.Add(r);
            }
            return result.ToArray();
        }

        static bool AncestorMatches(Transform t, string keyword, int maxDepth)
        {
            Transform node = t;
            for (int i = 0; i <= maxDepth && node != null; i++)
            {
                if (node.name.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                node = node.parent;
            }
            return false;
        }

        static bool HasTintableMaterial(Renderer[] renderers)
        {
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null && renderers[i].sharedMaterial != null && HasTintProperty(renderers[i].sharedMaterial))
                    return true;
            return false;
        }

        void UpdateHandTint()
        {
            if (!m_HandleTint) return;

            float fatigue = 1f - m_Controller.GetNormalizedStamina(m_Side);
            float amount = m_Config != null ? m_Config.handTintMax : 0.85f;
            Color red = m_Config != null ? m_Config.handTintColor : DefaultHandTint;

            for (int i = 0; i < m_TintMaterials.Length; i++)
            {
                var mat = m_TintMaterials[i];
                if (mat == null) continue;
                Color original = m_OriginalColors[i];
                Color target = Color.Lerp(original, red, fatigue * amount);
                target.a = original.a;
                if (m_LastTintColors == null || i >= m_LastTintColors.Length)
                {
                    WriteColor(mat, target);
                    continue;
                }
                float dr = target.r - m_LastTintColors[i].r;
                float dg = target.g - m_LastTintColors[i].g;
                float db = target.b - m_LastTintColors[i].b;
                float da = target.a - m_LastTintColors[i].a;
                if (dr * dr + dg * dg + db * db + da * da < 0.000001f) continue;
                m_LastTintColors[i] = target;
                WriteColor(mat, target);
            }
        }

        static bool HasTintProperty(Material mat)
        {
            return mat.HasProperty("_BaseColor") || mat.HasProperty("_Color");
        }

        static Color ReadColor(Material mat)
        {
            if (mat.HasProperty("_BaseColor")) return mat.GetColor("_BaseColor");
            if (mat.HasProperty("_Color")) return mat.GetColor("_Color");
            return Color.white;
        }

        static void WriteColor(Material mat, Color color)
        {
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            else if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        }
    }
}