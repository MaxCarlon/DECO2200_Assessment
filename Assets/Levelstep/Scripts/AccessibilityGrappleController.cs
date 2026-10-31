using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Climbing;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using Object = UnityEngine.Object;

namespace Levelstep
{
    [Serializable]
    public sealed class GrappleRendererLink
    {
        public Renderer trackedRenderer;
        public Renderer projectedRenderer;
    }

    /// <summary>Per-hand assisted climbing targeting and real XRI selection. Tracking poses are never rewritten.</summary>
    [DefaultExecutionOrder(-220)] // Tracked poses first; proxy pose before ClimbProvider (-210).
    [DisallowMultipleComponent]
    public class AccessibilityGrappleController : MonoBehaviour, IXRSelectFilter
    {
        public enum TargetPriority { ClosestToAim, Nearest, Balanced }
        [Flags] public enum DifficultyMask { Easy = 1, Medium = 2, Hard = 4 }
        public enum GrappleRejection
        {
            None, NoTarget, Disabled, ModeOff, MissingReferences, CalibrationRequired,
            MovementBusy, AlreadyHolding, Cooldown, Exhausted, BalanceLock, SupportRequired,
            InvalidHold, WrongRoute, WrongLayer, OutOfRange, AimAngle, BehindPlayer,
            WithinComfortableReach, Occluded, NormalGrabPriority, ApproachBlocked, GripReleased,
            SelectionUnavailable, WrongInteractionLayer
        }
        enum EligibilityPhase { Target, RetainedTarget, Pull, HeldFeedback }

        [Header("General")]
        public bool grapplingEnabled = true;
        [Tooltip("Presentation default: use configured reach values immediately; no calibration run is required.")]
        public bool enableWithoutCalibration = true;
        public ReachCalibrationSettings reachSettings;
        public SimulatedCalibrationSequence reachCalibration;
        [Tooltip("Off: use Reach Settings. On: use the three override values below. Missing Reach Settings also uses these fallback values.")]
        public bool overrideCalibratedValues;
        [Tooltip("Tracking-space metres from the head-relative reach origin. Used when overriding or when Reach Settings is missing.")]
        [Min(0f)] public float comfortableHorizontalReach = 0.45f;
        [Tooltip("Tracking-space vertical distance above OR below the reach origin. Used when overriding or when Reach Settings is missing.")]
        [Min(0f)] public float comfortableVerticalReach = 0.6f;
        [Tooltip("Acquisition limit in world metres from this tracked controller. A locked pull is not cancelled by later hand movement beyond this range; its line hides until back in range.")]
        [Min(0.01f)] public float maximumGrappleDistance = 1.5f;
        [Tooltip("Shared mode toggle, normally left X. Grabbing uses the existing physical interactor's Select input.")]
        public InputActionProperty activationInput = new InputActionProperty(new InputAction(
            "Grapple Mode", InputActionType.Button, "<XRController>{LeftHand}/primaryButton"));
        [HideInInspector] public bool toggleActivation; // Retained for serialized compatibility; mode always toggles.
        public bool grappleModeEnabledByDefault;
        [Tooltip("Leave empty on the mode owner. The other hand references that owner to share its X toggle.")]
        public AccessibilityGrappleController modeController;
        public XRBaseInputInteractor physicalInteractor;
        public AccessibilityGrappleMovement movement;

        [Header("Tracked references — never repositioned by this component")]
        public ClimbStaminaController.HandSide handSide = ClimbStaminaController.HandSide.Left;
        public Transform physicalController;
        public Transform aimTransform;
        public Transform trackedHead;
        public Transform trackingSpace;
        public Transform rigRoot;
        [Tooltip("Approximate shoulder/reach origin relative to tracked head yaw, in tracking-space metres.")]
        public Vector3 reachOriginOffset = new Vector3(-0.18f, -0.2f, 0f);
        public Vector3 aimEulerOffset;
        public XRInteractionManager interactionManager;
        public ClimbStaminaController stamina;
        public AccessibilityGrappleInteractor proxy;

        [Header("Targeting")]
        [Min(0.01f)] public float detectionRadius = 1.5f;
        [Tooltip("Broad acquisition cone HALF-angle. No direct ray hit is required. 60 means a 120-degree-wide area.")]
        [Range(1f, 85f)] public float targetingAngle = 60f;
        [Tooltip("Head-yaw cone half-angle. Allows cross-body/side holds while rejecting directly behind the player. 90 restores the old strict front hemisphere.")]
        [Range(90f, 179f)] public float maximumHeadTargetAngle = 135f;
        [Tooltip("Nearest is the accessibility default: prefer the next nearby eligible hold over precise aim.")]
        public TargetPriority targetPriority = TargetPriority.Nearest;
        [Tooltip("Required fractional score improvement before changing a valid preview target. 0 switches freely; 0.2 requires a 20% improvement. Higher is stickier.")]
        [Range(0f, 0.9f)] public float targetSwitchingSensitivity = 0.2f;
        [Tooltip("Wider HALF-angle for retaining the current preview. Never smaller than the acquisition cone.")]
        [Range(1f, 179f)] public float targetRetentionAngle = 75f;
        [Tooltip("Seconds to retain a valid preview after aim drifts outside its retention cone. Never bypasses range, route, stamina or collision checks. Grip locks independently of this timer.")]
        [Min(0f)] public float targetRetentionGraceDuration = 0.4f;
        public LayerMask validHoldLayers = ~0;
        public LayerMask obstacleLayers = ~0;
        [Min(0.01f)] public float targetingRefreshInterval = 0.05f;
        [Min(0f)] public float obstacleSurfaceTolerance = 0.015f;

        [Header("Visuals")]
        public bool showIndicators = true;
        public LineRenderer guidingLine;
        public Color guidingLineColor = new Color(0.4f, 1f, 0.85f);
        [Min(0.001f)] public float guidingLineWidth = 0.006f;
        public Color targetHighlightColor = new Color(0.35f, 1f, 1f);
        public Color connectedColor = new Color(0.3f, 1f, 0.35f);
        // Retained serialized references for existing scenes/upgrades. The original tracked model
        // now stays visible throughout: there is no projected model or render-pose override.
        [HideInInspector] public Transform projectedHand;
        [HideInInspector]
        public Renderer[] trackedHandRenderers = Array.Empty<Renderer>();
        [HideInInspector] public GrappleRendererLink[] rendererLinks = Array.Empty<GrappleRendererLink>();
        public bool connectionHaptics = true;

        [Header("Gameplay")]
        [Min(0f)] public float grappleCooldown = 0.3f;
        public bool grapplingConsumesStamina = true;
        [Tooltip("If enabled, the OTHER hand must already hold a climbing hold before this hand may grapple.")]
        public bool requiresExistingHandhold;
        public bool availableOnAllDifficulties = true;
        public DifficultyMask allowedDifficulties = DifficultyMask.Easy | DifficultyMask.Medium;

        [Header("Presentation debugging (Scene view Gizmos)")]
        public bool debugVisualizeEligibleTargets;
        [Min(0.005f)] public float debugTargetRadius = 0.035f;
        [Tooltip("Log rejected hold, reason, effective range and aim angle. Throttled; off by default.")]
        public bool debugLogRejections;
        [Min(0.1f)] public float debugLogInterval = 0.75f;

        readonly Collider[] m_Overlaps = new Collider[128];
        readonly RaycastHit[] m_OcclusionHits = new RaycastHit[128];
        readonly Vector3[] m_DebugPoints = new Vector3[128];
        int m_DebugCount;
        Collider m_TargetCollider;
        Vector3 m_TargetLocalPoint;
        ClimbInteractable m_SelectionTarget;
        bool m_Prepared, m_ActivateRequested, m_GripWasHeld, m_GripRequested, m_ModeEnabled;
        bool m_Pulling;
        ClimbInteractable m_PullTarget;
        float m_NextScan, m_NextGrapple;
        InputAction m_Action, m_OwnedAction;
        bool m_OwnsAction;
        float m_NextRejectionLog;
        float m_LastTargetAimTime = float.NegativeInfinity;
        XRBaseInputInteractor m_FilterInteractor;

        sealed class HighlightData
        {
            public Renderer[] renderers;
            public MaterialPropertyBlock[] originals;
            public MaterialPropertyBlock[] tinted;
        }
        readonly Dictionary<ClimbInteractable, HighlightData> m_Highlights = new Dictionary<ClimbInteractable, HighlightData>();
        static readonly Dictionary<ClimbInteractable, AccessibilityGrappleController> s_HighlightOwners = new Dictionary<ClimbInteractable, AccessibilityGrappleController>();
        readonly List<IXRInteractable> m_NormalTargets = new List<IXRInteractable>();
        readonly HashSet<ClimbInteractable> m_ScannedHolds = new HashSet<ClimbInteractable>();
        HighlightData m_Highlight;
        ClimbInteractable m_Highlighted;

        public ClimbInteractable CurrentTarget { get; private set; }
        public ClimbInteractable SelectionTarget => m_SelectionTarget;
        public bool IsGrappling => proxy != null && proxy.hasSelection;
        public bool IsPulling => m_Pulling;
        public ClimbInteractable LockedTarget => m_Pulling ? m_PullTarget : m_SelectionTarget;
        public bool GrappleModeEnabled => modeController != null && modeController != this ?
            modeController.isActiveAndEnabled && modeController.grapplingEnabled && modeController.GrappleModeEnabled : m_ModeEnabled;
        public bool GripHeld => physicalInteractor != null && physicalInteractor.isActiveAndEnabled && physicalInteractor.selectInput.ReadIsPerformed();
        public Vector3 TargetPoint => m_TargetCollider != null ? m_TargetCollider.transform.TransformPoint(m_TargetLocalPoint) : Vector3.zero;
        public string LastFeedback { get; private set; } = "Grapple Mode off";
        public GrappleRejection LastRejection { get; private set; }
        public bool WantsSelection => isActiveAndEnabled && grapplingEnabled && (m_Prepared || IsGrappling) && m_SelectionTarget != null;
        public float HorizontalReach => Mathf.Max(0f, !overrideCalibratedValues && reachSettings != null ? reachSettings.ComfortableHorizontalReachDistance : comfortableHorizontalReach);
        public float VerticalReach => Mathf.Max(0f, !overrideCalibratedValues && reachSettings != null ? reachSettings.ComfortableVerticalReachDistance : comfortableVerticalReach);
        public float MaxDistance => Mathf.Max(0f, !overrideCalibratedValues && reachSettings != null ?
            Mathf.Min(maximumGrappleDistance, reachSettings.MaximumGrapplingDistance) : maximumGrappleDistance);
        public float EffectiveGrappleRange => Mathf.Max(0f, Mathf.Min(detectionRadius, MaxDistance));

        void Awake()
        {
            m_ModeEnabled = grappleModeEnabledByDefault;
            LastFeedback = m_ModeEnabled ? "Grapple Mode on — use either grip" : "Grapple Mode off";
            if (projectedHand != null) projectedHand.gameObject.SetActive(false);
            if (guidingLine != null) { guidingLine.positionCount = 2; guidingLine.useWorldSpace = true; guidingLine.enabled = false; }
        }

        void Start()
        {
            if (interactionManager == null) interactionManager = Object.FindAnyObjectByType<XRInteractionManager>();
            if (stamina == null) stamina = ClimbStaminaController.Instance;
            RegisterPhysicalGrabFilter();
        }

        void OnEnable()
        {
            DifficultyRouteController.SelectionChanged += OnRouteChanged;
            if (Application.isPlaying && (modeController == null || modeController == this)) BindInput();
            if (Application.isPlaying) RegisterPhysicalGrabFilter();
        }
        void OnDisable()
        {
            DifficultyRouteController.SelectionChanged -= OnRouteChanged;
            RemovePhysicalGrabFilter();
            ReleaseInput(); CancelPull(); ReleaseGrapple(); ClearTarget();
            if (guidingLine != null) guidingLine.enabled = false;
        }
        void OnDestroy()
        {
            RemovePhysicalGrabFilter();
            ReleaseInput();
            var owned = m_OwnedAction ?? (activationInput.reference == null ? activationInput.action : null);
            owned?.Dispose();
        }

        void Update()
        {
            if (m_ActivateRequested) SetGrappleMode(!GrappleModeEnabled);
            m_ActivateRequested = false;
            bool held = GripHeld;
            m_GripRequested = held && !m_GripWasHeld;
            m_GripWasHeld = held;
            if (!held)
            {
                CancelPull();
                if (IsGrappling) ReleaseGrapple();
            }
            if (!grapplingEnabled)
            {
                CancelPull(); ReleaseGrapple(); ClearTarget();
                m_GripRequested = false;
                return;
            }
            SyncProxyPose();
            if (IsGrappling && (physicalController == null || !physicalController.gameObject.activeInHierarchy || !RouteAllowed(m_SelectionTarget))) ReleaseGrapple();
            if (m_Pulling && !CanContinuePull()) CancelPull();
            if (!GrappleModeEnabled && !IsGrappling && !m_Pulling) ClearTarget();
            else if (!IsGrappling && !m_Pulling && Time.unscaledTime >= m_NextScan) RefreshTarget();
        }

        void LateUpdate()
        {
            // XRI has processed ordinary physical grabs first. Never steal a successful normal grab.
            if (m_GripRequested && GripHeld) TryGrapple();
            m_GripRequested = false;
            var target = IsGrappling ? m_SelectionTarget : m_Pulling ? m_PullTarget : CurrentTarget;
            var phase = IsGrappling ? EligibilityPhase.HeldFeedback : m_Pulling ? EligibilityPhase.Pull : EligibilityPhase.RetainedTarget;
            var rejection = GrappleRejection.NoTarget;
            bool valid = target != null && EvaluateEligibility(target, m_TargetCollider, TargetPoint, phase, out rejection);
            if (!valid && target != null)
            {
                ReportRejection(target, TargetPoint, rejection);
                if (!IsGrappling && !m_Pulling) { ClearTarget(); m_NextScan = 0f; }
            }
            else if (valid) LastRejection = GrappleRejection.None;
            else if (!CheckState(phase, out var stateRejection)) ReportRejection(null, Vector3.zero, stateRejection);
            // Acquisition range is not a cancellation rule for an established lock. Keep pulling,
            // but honour the user's range-honest feedback policy even if the hand moves away.
            bool inRange = valid && IsWithinGrappleRange(target, TargetPoint);
            if (valid && !inRange) ReportRejection(target, TargetPoint, GrappleRejection.OutOfRange);
            UpdateHighlight(showIndicators && valid && inRange ? target : null);
            bool visible = showIndicators && valid && inRange;
            if (guidingLine == null) return;
            guidingLine.enabled = visible;
            if (!visible) return;
            var color = IsGrappling ? connectedColor : guidingLineColor;
            guidingLine.startColor = guidingLine.endColor = color;
            guidingLine.startWidth = guidingLine.endWidth = guidingLineWidth;
            guidingLine.SetPosition(0, physicalController.position);
            guidingLine.SetPosition(1, m_TargetCollider.transform.TransformPoint(m_TargetLocalPoint));
        }

        bool CheckState(EligibilityPhase phase, out GrappleRejection rejection)
        {
            bool acquiring = phase == EligibilityPhase.Target || phase == EligibilityPhase.RetainedTarget;
            rejection = GrappleRejection.None;
            if (!isActiveAndEnabled || !grapplingEnabled) rejection = GrappleRejection.Disabled;
            else if (!GrappleModeEnabled) rejection = GrappleRejection.ModeOff;
            else if (physicalController == null || !physicalController.gameObject.activeInHierarchy || physicalInteractor == null || !physicalInteractor.isActiveAndEnabled ||
                trackedHead == null || trackingSpace == null || proxy == null || !proxy.isActiveAndEnabled || interactionManager == null || stamina == null || movement == null || !movement.isActiveAndEnabled)
                rejection = GrappleRejection.MissingReferences;
            else if (acquiring && !enableWithoutCalibration && (reachCalibration == null || reachCalibration.LastOutcome != SimulatedCalibrationSequence.Outcome.Completed))
                rejection = GrappleRejection.CalibrationRequired;
            if (rejection != GrappleRejection.None) return false;

            var hand = stamina.GetHand(handSide);
            if (stamina.BalanceLock) rejection = GrappleRejection.BalanceLock;
            else if (stamina.HandStaminaEnabled && (hand.exhausted || hand.stamina <= 0f)) rejection = GrappleRejection.Exhausted;
            else if (phase == EligibilityPhase.HeldFeedback)
            {
                if (!IsGrappling) rejection = GrappleRejection.AlreadyHolding;
            }
            else if (physicalInteractor.hasSelection || hand.gripping || IsGrappling) rejection = GrappleRejection.AlreadyHolding;
            else if (movement.IsMoving && (acquiring || movement.ActiveHand != this)) rejection = GrappleRejection.MovementBusy;
            else if (acquiring)
            {
                if (m_Pulling) rejection = GrappleRejection.MovementBusy;
                else if (Time.unscaledTime < m_NextGrapple) rejection = GrappleRejection.Cooldown;
                else if (requiresExistingHandhold && !stamina.GetHand(handSide == ClimbStaminaController.HandSide.Left ? ClimbStaminaController.HandSide.Right : ClimbStaminaController.HandSide.Left).gripping)
                    rejection = GrappleRejection.SupportRequired;
                else if (HasNormalGrabPriority()) rejection = GrappleRejection.NormalGrabPriority;
            }
            else if (!m_Pulling || !GripHeld) rejection = GrappleRejection.GripReleased;
            return rejection == GrappleRejection.None;
        }

        public bool IsBeyondComfortableReach(Vector3 worldPoint)
        {
            if (trackingSpace == null || trackedHead == null) return false;
            var delta = ReachDelta(worldPoint);
            return new Vector2(delta.x, delta.z).magnitude > HorizontalReach || Mathf.Abs(delta.y) > VerticalReach;
        }

        Vector3 ReachDelta(Vector3 worldPoint)
        {
            Vector3 forward = trackingSpace.InverseTransformDirection(trackedHead.forward);
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            var origin = trackingSpace.InverseTransformPoint(trackedHead.position) + Quaternion.LookRotation(forward, Vector3.up) * reachOriginOffset;
            return trackingSpace.InverseTransformPoint(worldPoint) - origin;
        }

        public Vector3 RequiredBodyMotion()
        {
            return RequiredBodyMotion(m_PullTarget);
        }

        public Vector3 RequiredBodyMotion(ClimbInteractable target)
        {
            if (target == null || physicalController == null) return Vector3.zero;
            // Both points are world-space. Move the body by the *tracked hand's* remaining
            // translation, including its full vertical component, not the shoulder envelope.
            // Use exactly the hold's normal distance convention (including collider pivots).
            return target.GetDistance(physicalController.position).point - physicalController.position;
        }

        public bool IsAtNormalGrabPosition()
        {
            if (m_PullTarget == null || physicalController == null) return false;
            float distance = Mathf.Max(0f, m_PullTarget.maxInteractionDistance);
            return m_PullTarget.GetDistance(physicalController.position).distanceSqr <= distance * distance;
        }

        Vector3 AimDirection => (aimTransform != null ? aimTransform : physicalController).TransformDirection(Quaternion.Euler(aimEulerOffset) * Vector3.forward).normalized;

        public void RefreshTarget()
        {
            if (m_Pulling || IsGrappling) return;
            m_NextScan = Time.unscaledTime + Mathf.Max(0.01f, targetingRefreshInterval);
            m_DebugCount = 0;
            if (!CheckState(EligibilityPhase.Target, out var stateRejection))
            { ReportRejection(null, Vector3.zero, stateRejection); ClearTarget(); return; }
            float radius = EffectiveGrappleRange;
            LastRejection = GrappleRejection.None;
            var previous = CurrentTarget;
            var previousCollider = m_TargetCollider;
            var previousPoint = TargetPoint;
            bool retainPrevious = previous != null && EvaluateEligibility(previous, previousCollider, previousPoint, EligibilityPhase.RetainedTarget, out _);
            float previousScore = retainPrevious ? TargetScore(previousPoint) : float.MaxValue;
            int count = Physics.OverlapSphereNonAlloc(physicalController.position, radius, m_Overlaps, validHoldLayers, QueryTriggerInteraction.Ignore);
            m_ScannedHolds.Clear();
            float bestScore = float.MaxValue;
            ClimbInteractable best = null;
            Collider bestCollider = null;
            Vector3 bestPoint = default;
            for (int i = 0; i < count; i++)
            {
                var collider = m_Overlaps[i];
                if (!TryDetectCandidate(collider, out var climb, out var point) || !m_ScannedHolds.Add(climb)) continue;
                // New candidates use the acquisition cone. The old target has its own wider
                // retention/grace policy and is compared below, not dropped at every rescan.
                if (!EvaluateEligibility(climb, collider, point, EligibilityPhase.Target, out var rejection)) { ReportRejection(climb, point, rejection); continue; }
                if (m_DebugCount < m_DebugPoints.Length) m_DebugPoints[m_DebugCount++] = point;
                float score = TargetScore(point);
                if (score >= bestScore) continue;
                bestScore = score; best = climb; bestCollider = collider; bestPoint = point;
            }
            if (retainPrevious && (best == null || best == previous || bestScore >= previousScore * (1f - Mathf.Clamp01(targetSwitchingSensitivity))))
            { best = previous; bestCollider = previousCollider; bestPoint = previousPoint; }
            CurrentTarget = best; m_TargetCollider = bestCollider;
            if (bestCollider != null)
            {
                m_TargetLocalPoint = bestCollider.transform.InverseTransformPoint(bestPoint);
                if (best != previous) m_LastTargetAimTime = Time.unscaledTime;
            }
            else if (LastRejection == GrappleRejection.None) ReportRejection(null, Vector3.zero, GrappleRejection.NoTarget);
            UpdateHighlight(showIndicators ? best : null);
        }

        float TargetScore(Vector3 point)
        {
            float angle = Vector3.Angle(AimDirection, point - physicalController.position) / Mathf.Max(1f, targetingAngle);
            float distance = Vector3.Distance(physicalController.position, point) / Mathf.Max(0.01f, EffectiveGrappleRange);
            return targetPriority == TargetPriority.Nearest ? distance + angle * 0.001f :
                targetPriority == TargetPriority.Balanced ? angle + distance : angle + distance * 0.001f;
        }

        bool TryDetectCandidate(Collider collider, out ClimbInteractable climb, out Vector3 point)
        {
            climb = collider != null ? collider.GetComponentInParent<ClimbInteractable>() : null;
            point = Vector3.zero;
            if (climb == null) return false;
            // Overlap + broad cone, not a precision ray. A stable normal grab point avoids
            // changing the endpoint whenever a tiny aim movement hits a different mesh face.
            // Works identically for both hands and for one-sided non-convex hold meshes.
            point = climb.GetDistance(physicalController.position).point;
            return true;
        }

        public bool CanGrapple(ClimbInteractable target, Collider collider, Vector3 point, out GrappleRejection rejection)
        {
            return EvaluateEligibility(target, collider, point, target == CurrentTarget ? EligibilityPhase.RetainedTarget : EligibilityPhase.Target, out rejection);
        }

        bool EvaluateEligibility(ClimbInteractable target, Collider collider, Vector3 point, EligibilityPhase phase, out GrappleRejection rejection)
        {
            bool acquiring = phase == EligibilityPhase.Target || phase == EligibilityPhase.RetainedTarget;
            if (!CheckState(phase, out rejection)) return false;
            if (target == null || !target.isActiveAndEnabled || target.GetComponent<LevelstepHold>() == null ||
                collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy || collider.GetComponentInParent<ClimbInteractable>() != target)
                rejection = GrappleRejection.InvalidHold;
            else if (!RouteAllowed(target)) rejection = GrappleRejection.WrongRoute;
            else if ((validHoldLayers.value & (1 << collider.gameObject.layer)) == 0) rejection = GrappleRejection.WrongLayer;
            else if ((proxy.interactionLayers.value & target.interactionLayers.value) == 0) rejection = GrappleRejection.WrongInteractionLayer;
            else if (phase != EligibilityPhase.HeldFeedback && target.selectMode == InteractableSelectMode.Single && target.isSelected)
                rejection = GrappleRejection.SelectionUnavailable;
            else
            {
                Vector3 grabPoint = target.GetDistance(physicalController.position).point;
                float angle = Vector3.Angle(AimDirection, point - physicalController.position);
                bool headCone = !acquiring || InHeadTargetCone(point);
                bool retainedAim = phase == EligibilityPhase.RetainedTarget && target == CurrentTarget;
                if (retainedAim && angle <= Mathf.Max(targetingAngle, targetRetentionAngle) && headCone) m_LastTargetAimTime = Time.unscaledTime;
                bool aimGrace = retainedAim && Time.unscaledTime - m_LastTargetAimTime <= Mathf.Max(0f, targetRetentionGraceDuration);
                if (acquiring && !IsWithinGrappleRange(target, point))
                    rejection = GrappleRejection.OutOfRange;
                else if (acquiring && angle > (retainedAim ? Mathf.Max(targetingAngle, targetRetentionAngle) : targetingAngle) && !aimGrace)
                    rejection = GrappleRejection.AimAngle;
                else if (acquiring && !headCone && !aimGrace) rejection = GrappleRejection.BehindPlayer;
                else if (acquiring && !IsBeyondComfortableReach(point)) rejection = GrappleRejection.WithinComfortableReach;
                else if (acquiring && (!ClearLineOfSight(target, point) || ((point - grabPoint).sqrMagnitude > 0.000001f && !ClearLineOfSight(target, grabPoint)))) rejection = GrappleRejection.Occluded;
                else if (acquiring && !movement.CanApproach(this, target)) rejection = GrappleRejection.ApproachBlocked;
                // Locked movement/held selections deliberately do not re-acquire aim, range,
                // comfort or hand-ray visibility. Real body/head collisions still stop movement
                // in AccessibilityGrappleMovement; normal gameplay invalidations remain above.
            }
            return rejection == GrappleRejection.None;
        }

        bool IsWithinGrappleRange(ClimbInteractable target, Vector3 point)
        {
            if (target == null || physicalController == null) return false;
            Vector3 grabPoint = target.GetDistance(physicalController.position).point;
            float distanceSqr = Mathf.Max((point - physicalController.position).sqrMagnitude, (grabPoint - physicalController.position).sqrMagnitude);
            float range = EffectiveGrappleRange;
            return range > 0f && !float.IsNaN(distanceSqr) && !float.IsInfinity(distanceSqr) && distanceSqr <= range * range;
        }

        bool InHeadTargetCone(Vector3 point)
        {
            Vector3 forward = Vector3.ProjectOnPlane(trackedHead.forward, trackingSpace.up);
            if (forward.sqrMagnitude < 0.001f) forward = trackingSpace.forward;
            Vector3 direction = Vector3.ProjectOnPlane(point - trackedHead.position, trackingSpace.up);
            return direction.sqrMagnitude < 0.000001f || Vector3.Angle(forward, direction) <= maximumHeadTargetAngle;
        }

        bool HasNormalGrabPriority()
        {
            m_NormalTargets.Clear(); physicalInteractor.GetValidTargets(m_NormalTargets);
            foreach (var target in m_NormalTargets)
                if (target is IXRSelectInteractable selectable && interactionManager.IsSelectPossible(physicalInteractor, selectable)) return true;
            return false;
        }

        void ReportRejection(ClimbInteractable target, Vector3 point, GrappleRejection rejection)
        {
            LastRejection = rejection;
            if (!debugLogRejections || rejection == GrappleRejection.None || Time.unscaledTime < m_NextRejectionLog) return;
            m_NextRejectionLog = Time.unscaledTime + Mathf.Max(0.1f, debugLogInterval);
            float distance = physicalController != null && target != null ? Mathf.Max(Vector3.Distance(point, physicalController.position),
                Vector3.Distance(target.GetDistance(physicalController.position).point, physicalController.position)) : 0f;
            float angle = physicalController != null && target != null ? Vector3.Angle(AimDirection, point - physicalController.position) : 0f;
            Debug.Log($"[Grapple {handSide}] Rejected {(target != null ? target.name : "target")}: {rejection}; distance={distance:F2}m, effectiveRange={EffectiveGrappleRange:F2}m, aimAngle={angle:F1}deg.", this);
        }

        bool RouteAllowed(ClimbInteractable climb)
        {
            if (climb == null || !climb.isActiveAndEnabled) return false;
            var tag = climb.GetComponent<HoldRouteTag>();
            if (tag == null) return false;
            int selected = DifficultyRouteController.Instance != null ? DifficultyRouteController.Instance.SelectedIndex : stamina != null ? stamina.DifficultyIndex : -1;
            return (int)tag.routeType == selected && (availableOnAllDifficulties || ((int)allowedDifficulties & (1 << selected)) != 0);
        }

        bool ClearLineOfSight(ClimbInteractable climb, Vector3 point)
        {
            var delta = point - physicalController.position;
            int count = Physics.RaycastNonAlloc(physicalController.position, delta.normalized, m_OcclusionHits, delta.magnitude + 0.01f, obstacleLayers, QueryTriggerInteraction.Ignore);
            if (count == m_OcclusionHits.Length) return false; // A saturated occlusion query cannot establish visibility.
            for (int i = 0; i < count; i++)
            {
                var hit = m_OcclusionHits[i];
                if (rigRoot != null && hit.collider.transform.IsChildOf(rigRoot)) continue;
                if (hit.collider.GetComponentInParent<ClimbInteractable>() == climb) continue;
                if (hit.distance < delta.magnitude - obstacleSurfaceTolerance) return false;
            }
            return true;
        }

        public bool TryGrapple()
        {
            if (!Application.isPlaying || !GripHeld) return false;
            // Lock the hold already shown to the player, including its valid aim grace.
            // Only discover again when there is no usable preview; never rescore a valid grip.
            if (CurrentTarget == null || !CanGrapple(CurrentTarget, m_TargetCollider, TargetPoint, out _)) RefreshTarget();
            if (!CanGrapple(CurrentTarget, m_TargetCollider, TargetPoint, out var rejection))
            { ReportRejection(CurrentTarget, TargetPoint, rejection); return false; }
            m_PullTarget = CurrentTarget;
            m_Pulling = true;
            if (!movement.BeginPull(this))
            {
                m_Pulling = false; m_PullTarget = null;
                LastFeedback = "No collision-safe approach";
                return false;
            }
            LastFeedback = "Pulling — release grip to cancel";
            return true;
        }

        public bool CanContinuePull()
        {
            bool valid = EvaluateEligibility(m_PullTarget, m_TargetCollider, TargetPoint, EligibilityPhase.Pull, out var rejection);
            if (!valid) ReportRejection(m_PullTarget, TargetPoint, rejection);
            return valid;
        }

        public bool AttachAfterPull()
        {
            if (!CanContinuePull() || !IsAtNormalGrabPosition()) return false;
            m_SelectionTarget = m_PullTarget;
            m_Prepared = true;
            // The helper is always 1:1 with tracking. If normal distance/stamina/selection
            // filters reject this position, fail; never bridge the gap with a hand offset.
            SyncProxyPose();
            if (!interactionManager.IsSelectPossible(proxy, m_SelectionTarget)) { ResetConnection(); return false; }
            interactionManager.SelectEnter((UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor)proxy, m_SelectionTarget);
            m_Prepared = false;
            if (!IsGrappling) { ResetConnection(); return false; }
            if (connectionHaptics)
            {
                try { HapticsUtility.SendHapticImpulse(0.4f, 0.08f, handSide == ClimbStaminaController.HandSide.Left ? HapticsUtility.Controller.Left : HapticsUtility.Controller.Right); }
                catch (Exception error) { Debug.LogWarning("[Grapple] Connection haptic unavailable: " + error.Message); }
            }
            return true;
        }

        public void FinishPull(bool attached, string feedback)
        {
            m_Pulling = false; m_PullTarget = null;
            m_NextGrapple = Time.unscaledTime + Mathf.Max(0f, grappleCooldown);
            LastFeedback = feedback;
            if (!attached) ClearTarget();
        }

        public void CancelPull()
        {
            if (movement != null && movement.ActiveHand == this) movement.CancelPull("Pull cancelled");
        }

        public void SetGrappleMode(bool enabled)
        {
            if (modeController != null && modeController != this) { modeController.SetGrappleMode(enabled); return; }
            m_ModeEnabled = enabled;
            LastFeedback = enabled ? "Grapple Mode on — use either grip" : "Grapple Mode off";
            if (!enabled && movement != null) movement.CancelPull("Grapple Mode off");
            // Existing selections keep following their normal grip-release path.
        }

        public void SyncProxyPose()
        {
            if (proxy == null || physicalController == null) return;
            // This is an untracked selection helper, not a second controller pose. There is no
            // connected/support-hand exception, retained displacement or offset to accumulate.
            proxy.transform.SetPositionAndRotation(physicalController.position, physicalController.rotation);
        }

        public void ReleaseGrapple()
        {
            CancelPull();
            if (proxy != null && proxy.hasSelection && interactionManager != null && m_SelectionTarget != null)
                interactionManager.SelectExit((UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor)proxy, m_SelectionTarget);
            ResetConnection();
        }

        public void NotifySelectionExited()
        {
            m_NextGrapple = Time.unscaledTime + Mathf.Max(0f, grappleCooldown);
            ResetConnection(); // Selection events are complete before the next pose synchronization.
        }

        void ResetConnection()
        {
            m_Prepared = false; m_SelectionTarget = null;
            ClearTarget();
        }

        void OnRouteChanged(int index)
        {
            UpdateHighlight(null); // Runs before DifficultyRouteController applies its new property blocks.
            CancelPull();
            if (IsGrappling && m_SelectionTarget != null && (int)m_SelectionTarget.GetComponent<HoldRouteTag>().routeType != index) ReleaseGrapple();
            ClearTarget(); m_NextScan = 0f;
        }

        void ClearTarget() { CurrentTarget = null; m_LastTargetAimTime = float.NegativeInfinity; if (!IsGrappling && !m_Pulling) m_TargetCollider = null; UpdateHighlight(null); }

        void UpdateHighlight(ClimbInteractable target)
        {
            if (m_Highlighted == target) return;
            if (m_Highlight != null)
                for (int i = 0; i < m_Highlight.renderers.Length; i++)
                    if (m_Highlight.renderers[i] != null) m_Highlight.renderers[i].SetPropertyBlock(m_Highlight.originals[i]);
            if (!ReferenceEquals(m_Highlighted, null)) s_HighlightOwners.Remove(m_Highlighted);
            m_Highlight = null; m_Highlighted = null;
            if (target == null) return;
            // Two hands may aim at the same hold. Only one owns the original property block;
            // the other retries next frame instead of caching/restoring the first hand's tint.
            if (s_HighlightOwners.TryGetValue(target, out var owner) && owner != null && owner != this) return;
            s_HighlightOwners[target] = this;
            m_Highlighted = target;
            if (!m_Highlights.TryGetValue(target, out var data))
            {
                var renderers = target.GetComponentsInChildren<Renderer>(true);
                data = new HighlightData { renderers = renderers, originals = new MaterialPropertyBlock[renderers.Length], tinted = new MaterialPropertyBlock[renderers.Length] };
                for (int i = 0; i < renderers.Length; i++) { data.originals[i] = new MaterialPropertyBlock(); data.tinted[i] = new MaterialPropertyBlock(); }
                m_Highlights.Add(target, data);
            }
            m_Highlight = data;
            for (int i = 0; i < data.renderers.Length; i++)
            {
                var renderer = data.renderers[i]; if (renderer == null || renderer.sharedMaterial == null) continue;
                renderer.GetPropertyBlock(data.originals[i]); renderer.GetPropertyBlock(data.tinted[i]);
                Color color = targetHighlightColor; color.a = 1f;
                data.tinted[i].SetColor(renderer.sharedMaterial.HasProperty("_BaseColor") ? "_BaseColor" : "_Color", color);
                renderer.SetPropertyBlock(data.tinted[i]);
            }
        }

        public void RefreshConfiguration()
        {
            if (Application.isPlaying && isActiveAndEnabled) RegisterPhysicalGrabFilter();
            if (Application.isPlaying && isActiveAndEnabled && (modeController == null || modeController == this)) { ReleaseInput(); BindInput(); }
            m_NextScan = 0f; UpdateHighlight(null);
            if (!grapplingEnabled) { CancelPull(); ReleaseGrapple(); }
        }
        void BindInput()
        {
            m_Action = activationInput.action; m_OwnsAction = activationInput.reference == null;
            if (m_Action == null) return;
            if (m_OwnsAction && m_OwnedAction != m_Action) { m_OwnedAction?.Dispose(); m_OwnedAction = m_Action; }
            m_Action.performed += OnActivated;
            if (!m_Action.enabled) m_Action.Enable();
        }
        void ReleaseInput()
        {
            if (m_Action == null) return;
            m_Action.performed -= OnActivated;
            if (m_OwnsAction) m_Action.Disable(); m_Action = null;
            m_ActivateRequested = false;
        }
        void OnActivated(InputAction.CallbackContext context) => m_ActivateRequested = true;

        // A held physical grip must not automatically select an incidental nearby climbable
        // during travel and steal the locked approach. This narrow pre-selection filter affects
        // only this hand's physical climbing selections while pulling. UI, the other hand,
        // ordinary pre-grapple grabs and the zero-offset attachment helper are unaffected.
        public bool canProcess => isActiveAndEnabled && m_Pulling;
        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable) =>
            !m_Pulling || !ReferenceEquals(interactor, physicalInteractor) || !(interactable is ClimbInteractable);

        void RegisterPhysicalGrabFilter()
        {
            if (m_FilterInteractor == physicalInteractor) return;
            RemovePhysicalGrabFilter();
            m_FilterInteractor = physicalInteractor;
            if (m_FilterInteractor != null) m_FilterInteractor.selectFilters.Add(this);
        }

        void RemovePhysicalGrabFilter()
        {
            if (m_FilterInteractor != null) m_FilterInteractor.selectFilters.Remove(this);
            m_FilterInteractor = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetHighlightOwners() => s_HighlightOwners.Clear();

        void OnDrawGizmos()
        {
            if (!debugVisualizeEligibleTargets || !grapplingEnabled || !isActiveAndEnabled) return;
            Gizmos.color = targetHighlightColor;
            for (int i = 0; i < m_DebugCount; i++) Gizmos.DrawWireSphere(m_DebugPoints[i], debugTargetRadius);
        }
        void OnDrawGizmosSelected()
        {
            if (!debugVisualizeEligibleTargets || physicalController == null) return;
            Gizmos.color = new Color(0.3f, 1f, 1f, 0.4f);
            Gizmos.DrawWireSphere(physicalController.position, EffectiveGrappleRange);
            Gizmos.DrawRay(physicalController.position, AimDirection * EffectiveGrappleRange);
        }
    }
}
