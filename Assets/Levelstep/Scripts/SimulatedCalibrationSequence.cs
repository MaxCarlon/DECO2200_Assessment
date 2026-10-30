using UnityEngine;
using UnityEngine.InputSystem;

namespace Levelstep
{
    /// <summary>
    /// PROTOTYPE SIMULATION: this timed presentation does not measure balance, fabricate sensor
    /// readings, call BalanceBoardBridge.CalibrateFlat, or alter balance/stamina calculations.
    /// Reuse it with different text and the same CalibrationPanelView for other prototype sequences.
    /// </summary>
    [DisallowMultipleComponent]
    public class SimulatedCalibrationSequence : MonoBehaviour
    {
        public enum Phase { Idle = 0, CountingDown = 1, Complete = 2, WaitingForReady = 3 }
        public enum Outcome { None, Completed, Skipped, Cancelled }

        [Header("Prototype simulation — no sensor measurements")]
        public CalibrationPanelView panel;
        [Min(0f)] public float calibrationDuration = 3f;
        [Min(0f)] public float completionMessageDuration = 2f;
        [TextArea] public string instructionText = "Stand comfortably on the balance board.";
        [TextArea] public string completionText = "Calibration Complete";
        public bool useUnscaledTime = true;

        [Header("Activation — default: left secondary button (PICO Y)")]
        [Tooltip("Use an embedded Button action, or enable Use Reference to assign an existing action. Defaults to the unused left secondary face button.")]
        public InputActionProperty activationInput = new InputActionProperty(new InputAction(
            "Simulated Calibration", InputActionType.Button, "<XRController>{LeftHand}/secondaryButton"));
        [Tooltip("Press activation again to cancel/dismiss the active sequence. Otherwise repeat presses are ignored until it finishes.")]
        public bool activationCancelsActiveSequence = true;

        InputAction m_Activation;
        InputAction m_OwnedActivation;
        bool m_OwnsActivation;
        float m_Elapsed;
        public Phase CurrentPhase { get; private set; }
        public Outcome LastOutcome { get; private set; }
        public bool IsActive => CurrentPhase != Phase.Idle;
        public float RemainingSeconds => CurrentPhase == Phase.CountingDown ? Mathf.Max(0f, calibrationDuration - m_Elapsed) : 0f;
        public float SimulationProgress { get; private set; }

        void Awake()
        {
            // Always idle at load. Editor previews and controller input must never auto-start this demo.
            CurrentPhase = Phase.Idle;
            if (panel != null) { panel.RefreshAppearance(); panel.Hide(); }
        }

        void OnEnable()
        {
            if (Application.isPlaying) BindActivation();
        }

        void OnDisable()
        {
            ReleaseActivation();
            if (Application.isPlaying) CancelCalibration();
        }

        void OnDestroy()
        {
            ReleaseActivation();
            var owned = m_OwnedActivation ?? (activationInput.reference == null ? activationInput.action : null);
            owned?.Dispose();
        }

        void Update()
        {
            if (IsActive) Tick(useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);
        }

        void OnValidate()
        {
            calibrationDuration = Mathf.Max(0f, calibrationDuration);
            completionMessageDuration = Mathf.Max(0f, completionMessageDuration);
        }

        [ContextMenu("Run Simulated Calibration (Play Mode)")]
        public void BeginCalibration() => TryBeginCalibration();

        public bool TryBeginCalibration()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || IsActive) return false;
            m_Elapsed = 0f;
            SimulationProgress = 0f;
            LastOutcome = Outcome.None;
            CurrentPhase = Phase.WaitingForReady;
            if (panel != null) panel.ShowReady(instructionText);
            return true;
        }

        [ContextMenu("Confirm Ready (Play Mode)")]
        public void ConfirmReady()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || CurrentPhase != Phase.WaitingForReady) return;
            CurrentPhase = Phase.CountingDown;
            m_Elapsed = 0f;
            SimulationProgress = 0f;
            if (panel != null) panel.ShowInstructions(instructionText, RemainingSeconds, SimulationProgress);
            if (calibrationDuration <= 0f) EnterComplete(Outcome.Completed);
        }

        /// <summary>Explicit clock entry point for focused verification; this is time, never a sensor sample.</summary>
        public void Tick(float seconds)
        {
            if (!IsActive || CurrentPhase == Phase.WaitingForReady || seconds < 0f || float.IsNaN(seconds)) return;
            m_Elapsed += seconds;
            if (CurrentPhase == Phase.CountingDown)
            {
                if (m_Elapsed >= Mathf.Max(0f, calibrationDuration)) { EnterComplete(Outcome.Completed); return; }
                SimulationProgress = calibrationDuration > 0f ? Mathf.Clamp01(m_Elapsed / calibrationDuration) : 1f;
                if (panel != null) panel.UpdateCountdown(RemainingSeconds, SimulationProgress);
            }
            else if (m_Elapsed >= Mathf.Max(0f, completionMessageDuration))
            {
                CurrentPhase = Phase.Idle;
                if (panel != null) panel.Hide();
            }
        }

        [ContextMenu("Skip Countdown (Play Mode)")]
        public void SkipCalibration()
        {
            if (CurrentPhase == Phase.CountingDown) EnterComplete(Outcome.Skipped);
        }

        [ContextMenu("Cancel / Dismiss Simulation")]
        public void CancelCalibration()
        {
            if (CurrentPhase == Phase.WaitingForReady || CurrentPhase == Phase.CountingDown) LastOutcome = Outcome.Cancelled;
            CurrentPhase = Phase.Idle;
            m_Elapsed = 0f;
            SimulationProgress = 0f;
            if (panel != null) panel.Hide();
        }

        void EnterComplete(Outcome outcome)
        {
            LastOutcome = outcome;
            CurrentPhase = Phase.Complete;
            m_Elapsed = 0f;
            SimulationProgress = 1f;
            if (panel != null) panel.ShowComplete(completionText);
            if (completionMessageDuration <= 0f)
            {
                CurrentPhase = Phase.Idle;
                if (panel != null) panel.Hide();
            }
        }

        public void RefreshConfiguration()
        {
            if (Application.isPlaying && isActiveAndEnabled) { ReleaseActivation(); BindActivation(); }
            if (panel == null) return;
            panel.RefreshAppearance();
            if (CurrentPhase == Phase.WaitingForReady) panel.ShowReady(instructionText);
            else if (CurrentPhase == Phase.CountingDown) panel.ShowInstructions(instructionText, RemainingSeconds, SimulationProgress);
            else if (CurrentPhase == Phase.Complete) panel.ShowComplete(completionText);
        }

        void BindActivation()
        {
            m_Activation = activationInput.action;
            m_OwnsActivation = activationInput.reference == null;
            if (m_Activation == null) return;
            if (m_OwnsActivation && m_OwnedActivation != m_Activation)
            {
                m_OwnedActivation?.Dispose();
                m_OwnedActivation = m_Activation;
            }
            m_Activation.performed += OnActivated;
            if (!m_Activation.enabled) m_Activation.Enable();
        }

        void ReleaseActivation()
        {
            if (m_Activation == null) return;
            m_Activation.performed -= OnActivated;
            // Never disable another system's referenced action.
            if (m_OwnsActivation) m_Activation.Disable();
            m_Activation = null;
        }

        void OnActivated(InputAction.CallbackContext context)
        {
            if (IsActive)
            {
                if (activationCancelsActiveSequence) CancelCalibration();
                return;
            }
            TryBeginCalibration();
        }
    }
}
