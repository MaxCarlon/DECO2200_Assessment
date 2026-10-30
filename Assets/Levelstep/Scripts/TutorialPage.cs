using System;
using TMPro;
using UnityEngine;

namespace Levelstep
{
    [Serializable]
    public sealed class TutorialImagePadding
    {
        [Min(0f)] public float left = 12f;
        [Min(0f)] public float right = 12f;
        [Min(0f)] public float top = 12f;
        [Min(0f)] public float bottom = 12f;
    }

    [Serializable]
    public sealed class TutorialPageLayout
    {
        // Retained for existing serialized assets. Geometry is now authored only on RectTransforms.
        [HideInInspector] public Vector2 imagePosition = new Vector2(-370f, 0f);
        [HideInInspector] public Vector2 imageSize = new Vector2(680f, 680f);
        [Header("Image rendering")]
        [Tooltip("Image + Text rendering only. Full Image always fits the complete image without stretching or cropping.")]
        public ImageSequencePlayer.ScalingMode imageScaling = ImageSequencePlayer.ScalingMode.Fit;

        [Header("Title")]
        [HideInInspector] public Vector2 titlePosition = new Vector2(0f, 400f);
        [HideInInspector] public Vector2 titleSize = new Vector2(1420f, 110f);
        [Min(1f)] public float titleFontSize = 60f;
        public Color titleColor = Color.white;
        public TextAlignmentOptions titleAlignment = TextAlignmentOptions.Center;

        [Header("Description")]
        [HideInInspector] public Vector2 textPosition = new Vector2(370f, -20f);
        [HideInInspector] public Vector2 textSize = new Vector2(660f, 620f);
        [Min(1f)] public float textFontSize = 38f;
        public Color textColor = Color.white;
        public TextAlignmentOptions textAlignment = TextAlignmentOptions.TopLeft;
        [Tooltip("Optional per-page font; otherwise use the panel's default font.")]
        public TMP_FontAsset fontOverride;
    }

    [CreateAssetMenu(fileName = "TutorialPage", menuName = "Levelstep/Tutorial Page")]
    public class TutorialPage : ScriptableObject
    {
        public enum LayoutMode
        {
            [InspectorName("Image + Text")] ImageAndText = 0,
            [InspectorName("Full Image")] FullImage = 1
        }

        [Header("Page layout")]
        [Tooltip("Switch between independently Scene-authored image areas. Neither mode rewrites RectTransforms.")]
        public LayoutMode layoutMode = LayoutMode.ImageAndText;
        [Tooltip("Optional title in Full Image mode. Reserve room using Top padding or the Full Image Viewport RectTransform.")]
        public bool showTitleInFullImage;
        [Tooltip("Render padding inside the Full Image rectangle, in canvas UI units. This does not resize or reposition any RectTransform.")]
        public TutorialImagePadding fullImagePadding = new TutorialImagePadding();

        [Header("Content")]
        public string pageTitle = "New tutorial page";
        [TextArea(5, 16)] public string description = "";
        [Tooltip("Frames take priority over the static image. Null frames are skipped.")]
        public ImageSequenceSettings image = new ImageSequenceSettings();

        [Header("Content appearance")]
        [InspectorName("Override Content Appearance")]
        [Tooltip("Use this page's fonts, colours, alignment and image scaling. Positions and sizes always come from the authored RectTransforms.")]
        public bool overrideLayout;
        [InspectorName("Content Appearance")]
        public TutorialPageLayout layout = new TutorialPageLayout();

        [SerializeField, HideInInspector] string frameFolderPath = "";
        public string FrameFolderPath { get => frameFolderPath; set => frameFolderPath = value; }
    }
}
