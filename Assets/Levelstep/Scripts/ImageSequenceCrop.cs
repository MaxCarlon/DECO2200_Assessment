using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace Levelstep
{
    /// <summary>Centre-crops sprite UVs to fill an Image's authored rectangle without driving its layout.</summary>
    [RequireComponent(typeof(Image))]
    [DisallowMultipleComponent]
    public class ImageSequenceCrop : BaseMeshEffect
    {
        public override void ModifyMesh(VertexHelper vertices)
        {
            if (!IsActive() || !(graphic is Image image)) return;
            var sprite = image.overrideSprite;
            if (sprite == null) return;
            var rect = image.GetPixelAdjustedRect();
            if (rect.width <= 0f || rect.height <= 0f) return;

            var uv = DataUtility.GetOuterUV(sprite);
            float sourceAspect = sprite.rect.width / Mathf.Max(1f, sprite.rect.height);
            float targetAspect = rect.width / rect.height;
            if (sourceAspect > targetAspect)
            {
                float inset = (uv.z - uv.x) * (1f - targetAspect / sourceAspect) * 0.5f;
                uv.x += inset;
                uv.z -= inset;
            }
            else
            {
                float inset = (uv.w - uv.y) * (1f - sourceAspect / targetAspect) * 0.5f;
                uv.y += inset;
                uv.w -= inset;
            }

            vertices.Clear();
            vertices.AddVert(new Vector3(rect.xMin, rect.yMin, 0f), image.color, new Vector2(uv.x, uv.y));
            vertices.AddVert(new Vector3(rect.xMin, rect.yMax, 0f), image.color, new Vector2(uv.x, uv.w));
            vertices.AddVert(new Vector3(rect.xMax, rect.yMax, 0f), image.color, new Vector2(uv.z, uv.w));
            vertices.AddVert(new Vector3(rect.xMax, rect.yMin, 0f), image.color, new Vector2(uv.z, uv.y));
            vertices.AddTriangle(0, 1, 2);
            vertices.AddTriangle(2, 3, 0);
        }
    }
}
