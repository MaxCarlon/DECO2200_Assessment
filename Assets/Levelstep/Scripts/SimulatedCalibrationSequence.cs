using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Levelstep
{
    [Serializable]
    public sealed class SimulatedCalibrationStage
    {
        [TextArea] public string instructionText = "";
        [Min(0f)] public float duration = 3f;
    }

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

        [Header("Optional following stages (each requires I'm Ready)")]
        [Tooltip("The existing instruction/duration above are stage 1. Leave this empty for the balance calibration.")]
        public SimulatedCalibrationStage[] additionalStages = Array.Empty<SimulatedCalibrationStage>();
        [Tooltip("Default off. If enabled, show the first Ready step on load; a countdown still requires confirmation. Only one calibration may be open.")]
        public bool initiallyVisible;

        [Header("Activation")]
        [Tooltip("Use an embedded Button action, or Use Reference for an existing action. Balance uses left secondary/Y; reach uses right secondary/B.")]
        public InputActionProperty activationInput = new InputActionProperty(new InputAction(
            "Simulated Calibration", InputActionType.Button, "<XRController>{LeftHand}/secondaryButton"));
        [Tooltip("Press activation again to cancel/dismiss the active sequence. Otherwise repeat presses are ignored until it finishes.")]
        public bool activationCancelsActiveSequence = true;

        InputAction m_Activation;
        InputAction m_OwnedActivation;
        bool m_OwnsActivation;
        float m_Elapsed;
        bool m_AnyStageSkipped;
        static SimulatedCalibrationSequence s_ActiveSequence;
        public Phase CurrentPhase { get; private set; }
        public Outcome LastOutcome { get; private set; }
        public bool IsActive => CurrentPhase != Phase.Idle;
        public int CurrentStageIndex { get; private set; }
        public int StageCount => 1 + (additionalStages != null ? additionalStages.Length : 0);
        public float CurrentStageDuration => GetStageDuration(CurrentStageIndex);
        public string CurrentInstructionText => GetStageInstruction(CurrentStageIndex);
        public float RemainingSeconds => CurrentPhase == Phase.CountingDown ? Mathf.Max(0f, CurrentStageDuration - m_Elapsed) : 0f;
        public float SimulationProgress { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetPresentationOwner() => s_ActiveSequence = null;

        void Awake()
        {
            // Always idle initially. Opening an opt-in initial panel still cannot start a countdown.
            CurrentPhase = Phase.Idle;
            if (panel != null) { panel.RefreshAppearance(); panel.Hide(); }
        }

        void Start()
        {
            if (initiallyVisible) TryBeginCalibration();
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
            if (additionalStages != null)
                foreach (var stage in additionalStages)
                    if (stage != null) stage.duration = Mathf.Max(0f, stage.duration);
        }

        [ContextMenu("Run Simulated Calibration (Play Mode)")]
        public void BeginCalibration() => TryBeginCalibration();

        public bool TryBeginCalibration()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || IsActive) return false;
            // Presentation ownership only; this is not a persistent singleton or gameplay lock.
            if (s_ActiveSequence != null && s_ActiveSequence != this && s_ActiveSequence.IsActive) return false;
            s_ActiveSequence = this;
            CurrentStageIndex = 0;
            m_AnyStageSkipped = false;
            m_Elapsed = 0f;
            SimulationProgress = 0f;
            LastOutcome = Outcome.None;
            CurrentPhase = Phase.WaitingForReady;
            if (panel != null) panel.ShowReady(CurrentInstructionText);
            return true;
        }

        [ContextMenu("Confirm Ready (Play Mode)")]
        public void ConfirmReady()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || CurrentPhase != Phase.WaitingForReady) return;
            CurrentPhase = Phase.CountingDown;
            m_Elapsed = 0f;
            SimulationProgress = 0f;
            if (panel != null) panel.ShowInstructions(CurrentInstructionText, RemainingSeconds, SimulationProgress);
            if (CurrentStageDuration <= 0f) FinishStage(false);
        }

        /// <summary>Explicit clock entry point for focused verification; this is time, never a sensor sample.</summary>
        public void Tick(float seconds)
        {
            if (!IsActive || CurrentPhase == Phase.WaitingForReady || seconds < 0f || float.IsNaN(seconds)) return;
            m_Elapsed += seconds;
            if (CurrentPhase == Phase.CountingDown)
            {
                float duration = CurrentStageDuration;
                if (m_Elapsed >= duration) { FinishStage(false); return; }
                SimulationProgress = duration > 0f ? Mathf.Clamp01(m_Elapsed / duration) : 1f;
                if (panel != null) panel.UpdateCountdown(RemainingSeconds, SimulationProgress);
            }
            else if (m_Elapsed >= Mathf.Max(0f, completionMessageDuration))
            {
                CurrentPhase = Phase.Idle;
                ReleasePresentation();
                if (panel != null) panel.Hide();
            }
        }

        [ContextMenu("Skip Countdown (Play Mode)")]
        public void SkipCalibration()
        {
            if (CurrentPhase == Phase.CountingDown) FinishStage(true);
        }

        [ContextMenu("Cancel / Dismiss Simulation")]
        public void CancelCalibration()
        {
            if (CurrentPhase == Phase.WaitingForReady || CurrentPhase == Phase.CountingDown) LastOutcome = Outcome.Cancelled;
            CurrentPhase = Phase.Idle;
            ReleasePresentation();
            m_Elapsed = 0f;
            SimulationProgress = 0f;
            if (panel != null) panel.Hide();
        }

        void FinishStage(bool skipped)
        {
            m_AnyStageSkipped |= skipped;
            if (CurrentStageIndex + 1 < StageCount)
            {
                CurrentStageIndex++;
                CurrentPhase = Phase.WaitingForReady;
                m_Elapsed = 0f;
                SimulationProgress = 0f;
                if (panel != null) panel.ShowReady(CurrentInstructionText);
            }
            else EnterComplete(m_AnyStageSkipped ? Outcome.Skipped : Outcome.Completed);
        }

        void ReleasePresentation()
        {
            if (s_ActiveSequence == this) s_ActiveSequence = null;
        }

        public string GetStageInstruction(int index)
        {
            if (index <= 0) return instructionText;
            return additionalStages != null && index <= additionalStages.Length ? additionalStages[index - 1]?.instructionText ?? "" : "";
        }

        public float GetStageDuration(int index)
        {
            if (index <= 0) return Mathf.Max(0f, calibrationDuration);
            return additionalStages != null && index <= additionalStages.Length ? Mathf.Max(0f, additionalStages[index - 1]?.duration ?? 0f) : 0f;
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
                ReleasePresentation();
                if (panel != null) panel.Hide();
            }
        }

        public void RefreshConfiguration()
        {
            if (Application.isPlaying && isActiveAndEnabled) { ReleaseActivation(); BindActivation(); }
            if (panel == null) return;
            CurrentStageIndex = Mathf.Clamp(CurrentStageIndex, 0, StageCount - 1);
            panel.RefreshAppearance();
            if (CurrentPhase == Phase.WaitingForReady) panel.ShowReady(CurrentInstructionText);
            else if (CurrentPhase == Phase.CountingDown) panel.ShowInstructions(CurrentInstructionText, RemainingSeconds, SimulationProgress);
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
