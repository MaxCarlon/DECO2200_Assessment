using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace Levelstep
{
    /// <summary>Fit and centre the entire sprite inside padded, Scene-authored bounds. No layout driving.</summary>
    [RequireComponent(typeof(Image))]
    [DisallowMultipleComponent]
    public class TutorialFullImageFit : BaseMeshEffect
    {
        // Left, right, top, bottom. TutorialPage owns configuration; this component owns rendering only.
        [SerializeField, HideInInspector] Vector4 padding;

        public void SetPadding(TutorialImagePadding value)
        {
            Vector4 next = value == null ? Vector4.zero : new Vector4(
                Mathf.Max(0f, value.left), Mathf.Max(0f, value.right), Mathf.Max(0f, value.top), Mathf.Max(0f, value.bottom));
            if (padding == next) return;
            padding = next;
            if (graphic != null) graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vertices)
        {
            if (!IsActive() || !(graphic is Image image)) return;
            var sprite = image.overrideSprite;
            if (sprite == null) return;
            Rect bounds = image.GetPixelAdjustedRect();
            float width = Mathf.Max(0f, bounds.width - padding.x - padding.y);
            float height = Mathf.Max(0f, bounds.height - padding.z - padding.w);
            vertices.Clear();
            if (width <= 0f || height <= 0f || sprite.rect.width <= 0f || sprite.rect.height <= 0f) return;
            float scale = Mathf.Min(width / sprite.rect.width, height / sprite.rect.height);
            float imageWidth = sprite.rect.width * scale;
            float imageHeight = sprite.rect.height * scale;
            float x = bounds.xMin + padding.x + (width - imageWidth) * 0.5f;
            float y = bounds.yMin + padding.w + (height - imageHeight) * 0.5f;
            Vector4 uv = DataUtility.GetOuterUV(sprite);
            vertices.AddVert(new Vector3(x, y, 0f), image.color, new Vector2(uv.x, uv.y));
            vertices.AddVert(new Vector3(x, y + imageHeight, 0f), image.color, new Vector2(uv.x, uv.w));
            vertices.AddVert(new Vector3(x + imageWidth, y + imageHeight, 0f), image.color, new Vector2(uv.z, uv.w));
            vertices.AddVert(new Vector3(x + imageWidth, y, 0f), image.color, new Vector2(uv.z, uv.y));
            vertices.AddTriangle(0, 1, 2);
            vertices.AddTriangle(2, 3, 0);
        }
    }
}
