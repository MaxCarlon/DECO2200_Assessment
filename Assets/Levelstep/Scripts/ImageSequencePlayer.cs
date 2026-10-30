using System;
using UnityEngine;
using UnityEngine.UI;

namespace Levelstep
{
    [Serializable]
    public sealed class ImageSequenceSettings
    {
        public Sprite staticImage;
        public Sprite[] frames = Array.Empty<Sprite>();
        [Min(0f)] public float framesPerSecond = 3f;
        public bool loop = true;
        [Tooltip("Play forwards then backwards. A non-looping ping-pong plays one round trip.")]
        public bool pingPong;
        [Min(0f)] public float playbackSpeed = 1f;
        public bool autoPlay = true;
    }

    /// <summary>Allocation-free frame advancement over pre-imported sprites; no runtime texture creation.</summary>
    [DisallowMultipleComponent]
    public class ImageSequencePlayer : MonoBehaviour
    {
        public enum ScalingMode { Fit, Stretch, FillAndCrop }

        [Serializable]
        public struct PlaybackState
        {
            public double position;
            public bool playRequested;
        }

        [Header("Standalone playback (tutorial pages supply their own settings)")]
        [SerializeField] Image targetImage;
        // Preserve the old reference for serialized compatibility. The editor migration disables this
        // layout driver; image fitting/cropping now changes mesh/UV data, never RectTransforms.
        [SerializeField, HideInInspector] AspectRatioFitter aspectFitter;
        [SerializeField, HideInInspector] ImageSequenceCrop cropEffect;
        public ImageSequenceSettings sequence = new ImageSequenceSettings();
        public ScalingMode scalingMode = ScalingMode.Fit;
        public bool useUnscaledTime = true;
        [Min(0f)] public float speedMultiplier = 1f;

        Sprite[] m_Frames = Array.Empty<Sprite>();
        ImageSequenceSettings m_Settings;
        double m_Position;
        bool m_PlayRequested;
        bool m_Initialized;
        bool m_PresentationVisible = true;
        bool m_PlayWhileHidden;
        int m_LastRenderedFrame = -1;
        ScalingMode m_LastScaling = (ScalingMode)(-1);

        public int FrameCount => m_Frames.Length;
        public Image TargetImage => targetImage;
        public int CurrentFrame { get; private set; }
        public bool IsComplete => !Settings.loop && m_Position >= EndPosition && FrameCount > 1;
        public bool IsPlaying => m_PlayRequested && !IsComplete && FrameCount > 1;
        ImageSequenceSettings Settings => m_Settings ?? sequence;
        int EndPosition => Settings.pingPong ? Mathf.Max(0, 2 * (FrameCount - 1)) : Mathf.Max(0, FrameCount - 1);

        void Start()
        {
            if (!m_Initialized) SetSequence(sequence, true);
        }

        void Update() => Advance(useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);

        public void Bind(Image image, AspectRatioFitter fitter)
        {
            aspectFitter = fitter;
            SetTargetImage(image);
        }

        /// <summary>Route playback to another authored Image without changing its geometry or the clock.</summary>
        public void SetTargetImage(Image image)
        {
            targetImage = image;
            cropEffect = image != null ? image.GetComponent<ImageSequenceCrop>() : null;
            m_LastScaling = (ScalingMode)(-1);
            m_LastRenderedFrame = -1;
            if (targetImage != null) targetImage.raycastTarget = false;
        }

        public void SetSequence(ImageSequenceSettings settings, bool restart, PlaybackState state = default)
        {
            m_Settings = settings ?? new ImageSequenceSettings();
            int count = 0;
            var frames = m_Settings.frames;
            if (frames != null)
                for (int i = 0; i < frames.Length; i++)
                    if (frames[i] != null) count++;
            if (count > 0)
            {
                m_Frames = new Sprite[count];
                int next = 0;
                for (int i = 0; i < frames.Length; i++)
                    if (frames[i] != null) m_Frames[next++] = frames[i];
            }
            else
            {
                m_Frames = m_Settings.staticImage != null ? new[] { m_Settings.staticImage } : Array.Empty<Sprite>();
            }

            m_Initialized = true;
            m_Position = restart ? 0d : Math.Max(0d, state.position);
            m_PlayRequested = restart ? m_Settings.autoPlay : state.playRequested;
            m_LastRenderedFrame = -1;
            RenderCurrentFrame();
            RefreshTicking();
        }

        public PlaybackState CaptureState() => new PlaybackState { position = m_Position, playRequested = m_PlayRequested };

        public void SetPresentationVisible(bool visible, bool playWhileHidden)
        {
            m_PresentationVisible = visible;
            m_PlayWhileHidden = playWhileHidden;
            if (visible) RenderCurrentFrame();
            RefreshTicking();
        }

        public void SetScalingMode(ScalingMode mode)
        {
            scalingMode = mode;
            m_LastScaling = (ScalingMode)(-1);
            m_LastRenderedFrame = -1;
            RenderCurrentFrame();
        }

        [ContextMenu("Pause Playback")]
        public void Pause() { m_PlayRequested = false; RefreshTicking(); }

        [ContextMenu("Resume Playback")]
        public void Resume() { m_PlayRequested = true; RefreshTicking(); }

        [ContextMenu("Restart Playback")]
        public void RestartPlayback()
        {
            m_Position = 0d;
            m_PlayRequested = true;
            m_LastRenderedFrame = -1;
            RenderCurrentFrame();
            RefreshTicking();
        }

        /// <summary>Also usable by deterministic checks or an external clock. Hidden/paused policy is respected.</summary>
        public void Advance(float seconds)
        {
            if (!IsPlaying || (!m_PresentationVisible && !m_PlayWhileHidden) || seconds <= 0f) return;
            m_Position += (double)seconds * Mathf.Max(0f, Settings.framesPerSecond) * Mathf.Max(0f, Settings.playbackSpeed) * Mathf.Max(0f, speedMultiplier);
            if (Settings.loop)
            {
                int cycle = Settings.pingPong ? 2 * (FrameCount - 1) : FrameCount;
                m_Position %= cycle;
            }
            else m_Position = Math.Min(m_Position, EndPosition);

            UpdateFrameIndex();
            if (m_PresentationVisible) RenderCurrentFrame();
            RefreshTicking();
        }

        void UpdateFrameIndex()
        {
            if (FrameCount < 2) { CurrentFrame = 0; return; }
            double position = m_Position;
            int cycle = Settings.pingPong ? 2 * (FrameCount - 1) : FrameCount;
            int step = (int)Math.Floor(Settings.loop ? position % cycle : Math.Min(position, EndPosition));
            CurrentFrame = Settings.pingPong && step >= FrameCount ? cycle - step : step;
        }

        void RenderCurrentFrame()
        {
            UpdateFrameIndex();
            if (targetImage == null) return;
            if (FrameCount == 0)
            {
                targetImage.sprite = null;
                targetImage.enabled = false;
                return;
            }
            if (CurrentFrame == m_LastRenderedFrame) return;
            m_LastRenderedFrame = CurrentFrame;
            var sprite = m_Frames[CurrentFrame];
            targetImage.sprite = sprite;
            targetImage.enabled = true;
            bool scalingChanged = m_LastScaling != scalingMode;
            if (scalingChanged) targetImage.preserveAspect = scalingMode == ScalingMode.Fit;
            if (cropEffect == null) cropEffect = targetImage.GetComponent<ImageSequenceCrop>();
            if (cropEffect == null && scalingMode == ScalingMode.FillAndCrop)
                cropEffect = targetImage.gameObject.AddComponent<ImageSequenceCrop>();
            if (cropEffect != null)
            {
                bool crop = scalingMode == ScalingMode.FillAndCrop;
                if (cropEffect.enabled != crop) cropEffect.enabled = crop;
            }
            m_LastScaling = scalingMode;
        }

        void RefreshTicking()
        {
            bool shouldTick = Application.isPlaying && IsPlaying && Settings.framesPerSecond > 0f && Settings.playbackSpeed > 0f && speedMultiplier > 0f &&
                (m_PresentationVisible || m_PlayWhileHidden);
            if (enabled != shouldTick) enabled = shouldTick;
        }
    }
}
