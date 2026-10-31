using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Climbing;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;

namespace Levelstep
{
    /// <summary>One exclusive assisted approach, using the rig's existing collision-constrained XRI body movement.</summary>
    [DefaultExecutionOrder(-215)]
    public class AccessibilityGrappleMovement : LocomotionProvider, IGravityController
    {
        [Header("Assisted movement")]
        [Min(0.01f)] public float grappleMovementSpeed = 0.65f;
        [Tooltip("SmoothDamp acceleration/deceleration time in seconds. Zero uses constant speed.")]
        [Min(0f)] public float movementSmoothing = 0.2f;
        // Compatibility only: reaching the shoulder envelope used to leave a hand/hold gap.
        // The hold's unchanged normal grab distance now determines when attachment is possible.
        [HideInInspector] public float comfortableStoppingDistance = 0.38f;
        [HideInInspector] public float reachMargin = 0.025f;
        [Tooltip("Seconds before cancelling an unfinished pull. Zero disables the time limit.")]
        [Min(0f)] public float movementDurationLimit = 8f;
        [Header("Collision and comfort")]
        public LayerMask movementObstacleLayers = ~0;
        [Min(0.01f)] public float headClearanceRadius = 0.14f;
        [Min(0.001f)] public float collisionPadding = 0.01f;
        public ClimbProvider climbProvider;
        public GravityProvider gravityProvider;

        public AccessibilityGrappleController ActiveHand { get; private set; }
        public bool IsMoving => ActiveHand != null;
        public bool canProcess => isActiveAndEnabled;
        public bool gravityPaused => IsMoving;
        readonly List<LocomotionProvider> m_Providers = new List<LocomotionProvider>();
        readonly List<bool> m_Enabled = new List<bool>();
        readonly RaycastHit[] m_Hits = new RaycastHit[64];
        readonly Collider[] m_Overlap = new Collider[64];
        Vector3 m_Velocity;
        float m_Started;
        XRBodyTransformer m_Transformer;
        CharacterController m_Character;
        GuardedMovement m_Step;

        // A queued command may outlive a cancellation. Recheck the owner and grip at application time.
        sealed class GuardedMovement : IXRBodyTransformation
        {
            public AccessibilityGrappleMovement owner;
            public Vector3 motion;
            readonly XROriginMovement m_Movement = new XROriginMovement();
            public void Apply(XRMovableBody body)
            {
                if (!owner.IsMoving || !owner.ActiveHand.CanContinuePull() || !owner.HasCollisionSupport()) return;
                if (!owner.ClearPath(motion)) { owner.CancelPull("Approach obstructed"); return; }
                Vector3 before = body.originTransform.position;
                m_Movement.motion = motion;
                m_Movement.Apply(body);
                if (Vector3.Distance(body.originTransform.position - before, motion) > 0.004f)
                {
                    owner.CancelPull("Body collision stopped the pull");
                    return;
                }
                // Finish before the normal interactor's XRI processing this frame. Both select
                // paths use the same unchanged distance rule; no second climb step is queued.
                owner.TryFinishAtTrackedPosition();
            }
        }

        protected override void Awake()
        {
            base.Awake();
            m_Step = new GuardedMovement { owner = this };
        }

        bool HasCollisionSupport()
        {
            return mediator != null && mediator.xrOrigin != null && m_Transformer != null && m_Transformer.isActiveAndEnabled &&
                m_Transformer.constrainedBodyManipulator is CharacterControllerBodyManipulator &&
                m_Character != null && m_Character.enabled && m_Character.gameObject.activeInHierarchy;
        }

        public bool BeginPull(AccessibilityGrappleController hand)
        {
            if (IsMoving || !isActiveAndEnabled || hand == null || !hand.CanContinuePull() || mediator == null || mediator.xrOrigin == null) return false;
            if (!CanApproach(hand, hand.CurrentTarget)) return false;
            m_Velocity = Vector3.zero;
            m_Started = Time.unscaledTime;
            if (!TryStartLocomotionImmediately()) return false;
            ActiveHand = hand;
            m_Providers.Clear(); m_Enabled.Clear();
            mediator.xrOrigin.GetComponentsInChildren(true, m_Providers);
            foreach (var provider in m_Providers)
            {
                bool suspend = provider != this && !(provider is GravityProvider);
                m_Enabled.Add(provider.enabled);
                if (suspend) provider.enabled = false;
            }
            if (gravityProvider != null) { TryLockGravity(GravityOverride.ForcedOff); gravityProvider.ResetFallForce(); }
            hand.stamina.SetAssistedMovement(hand.handSide, true, hand.grapplingConsumesStamina);
            return true;
        }

        // Read-only preflight used by the same eligibility check for preview and activation.
        // Never changes a tracking pose, selection, provider state or locomotion ownership.
        public bool CanApproach(AccessibilityGrappleController hand, ClimbInteractable target)
        {
            if (!isActiveAndEnabled || hand == null || target == null || mediator == null || mediator.xrOrigin == null ||
                (IsMoving && ActiveHand != hand) || climbProvider == null || (!IsMoving && !climbProvider.isActiveAndEnabled)) return false;
            m_Transformer = mediator.GetComponent<XRBodyTransformer>();
            m_Character = mediator.xrOrigin.Origin.GetComponent<CharacterController>();
            if (!HasCollisionSupport()) return false; // Never fall back to collision-free transform translation.
            return ClearPath(hand.RequiredBodyMotion(target));
        }

        bool TryFinishAtTrackedPosition()
        {
            if (!IsMoving || !ActiveHand.CanContinuePull() || !ActiveHand.IsAtNormalGrabPosition()) return false;
            bool attached = ActiveHand.AttachAfterPull();
            EndPull(attached, attached ? "Connected at tracked hand — keep holding grip to climb" : "Attachment unavailable");
            return true;
        }

        void Update()
        {
            if (!IsMoving) return;
            if (!ActiveHand.CanContinuePull() || !HasCollisionSupport()) { CancelPull("Pull cancelled"); return; }
            if (movementDurationLimit > 0f && Time.unscaledTime - m_Started >= movementDurationLimit)
            { CancelPull("Movement time limit reached"); return; }
            // A new ordinary support grab may call ClimbProvider.StartClimbGrab while it is suspended.
            foreach (var provider in m_Providers)
                if (provider != null && provider != this && !(provider is GravityProvider)) provider.enabled = false;

            // Recalculate from current tracking, never from a projected hand or a previous
            // approach's destination. Physical hand motion during travel is included too.
            Vector3 position = mediator.xrOrigin.Origin.transform.position;
            Vector3 remaining = ActiveHand.RequiredBodyMotion();
            if (!ClearPath(remaining)) { CancelPull("Approach obstructed"); return; }
            if (TryFinishAtTrackedPosition()) return;
            Vector3 destination = position + remaining;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (dt <= 0f) return;
            Vector3 next = movementSmoothing <= 0f ? Vector3.MoveTowards(position, destination, grappleMovementSpeed * dt) :
                Vector3.SmoothDamp(position, destination, ref m_Velocity, movementSmoothing, grappleMovementSpeed, dt);
            // SmoothDamp's asymptotic final steps can fall below the existing CharacterController's
            // minMoveDistance and stall forever. Finish at a small, still speed-bounded step instead.
            float minimumStep = Mathf.Max(0.0002f, m_Character.minMoveDistance * 1.1f);
            if ((next - position).magnitude < minimumStep)
                next = Vector3.MoveTowards(position, destination, Mathf.Min(minimumStep, grappleMovementSpeed * dt));
            m_Step.motion = Vector3.ClampMagnitude(next - position, grappleMovementSpeed * dt);
            TryQueueTransformation(m_Step);
        }

        bool Ignore(Collider collider) => collider == null || collider.transform.IsChildOf(mediator.xrOrigin.transform);

        bool ClearPath(Vector3 motion)
        {
            var origin = mediator.xrOrigin;
            Vector3 up = origin.Origin.transform.up;
            float scale = Mathf.Abs(origin.Origin.transform.lossyScale.x);
            Vector3 ground = m_Transformer.bodyPositionEvaluator.GetBodyGroundWorldPosition(origin);
            float radius = m_Character.radius * scale + collisionPadding;
            float height = Mathf.Max(radius * 2f, Vector3.Dot(origin.Camera.transform.position - ground, up));
            Vector3 bottom = ground + up * (radius + m_Character.skinWidth * scale + collisionPadding);
            Vector3 top = ground + up * (Mathf.Max(radius, height - radius) + m_Character.skinWidth * scale + collisionPadding);
            Vector3 head = origin.Camera.transform.position;
            int count = Physics.OverlapCapsuleNonAlloc(bottom + motion, top + motion, radius, m_Overlap, movementObstacleLayers, QueryTriggerInteraction.Ignore);
            if (count == m_Overlap.Length) return false;
            for (int i = 0; i < count; i++) if (!Ignore(m_Overlap[i])) return false;
            count = Physics.OverlapSphereNonAlloc(head + motion, headClearanceRadius * scale, m_Overlap, movementObstacleLayers, QueryTriggerInteraction.Ignore);
            if (count == m_Overlap.Length) return false;
            for (int i = 0; i < count; i++) if (!Ignore(m_Overlap[i])) return false;
            if (motion.sqrMagnitude < 0.0000001f) return true;
            float length = motion.magnitude;
            count = Physics.CapsuleCastNonAlloc(bottom, top, radius, motion / length, m_Hits, length, movementObstacleLayers, QueryTriggerInteraction.Ignore);
            if (count == m_Hits.Length) return false;
            for (int i = 0; i < count; i++) if (!Ignore(m_Hits[i].collider)) return false;
            count = Physics.SphereCastNonAlloc(head, headClearanceRadius * scale, motion / length, m_Hits, length, movementObstacleLayers, QueryTriggerInteraction.Ignore);
            if (count == m_Hits.Length) return false;
            for (int i = 0; i < count; i++) if (!Ignore(m_Hits[i].collider)) return false;
            return true;
        }

        public void CancelPull(string feedback = "Pull cancelled") { if (IsMoving) EndPull(false, feedback); }

        void EndPull(bool attached, string feedback)
        {
            var hand = ActiveHand;
            ActiveHand = null; // Also invalidates queued movement before restoring any provider.
            hand.stamina.SetAssistedMovement(hand.handSide, false, false);
            TryEndLocomotion();
            for (int i = 0; i < m_Providers.Count; i++)
                if (m_Providers[i] != null && m_Providers[i] != this && !(m_Providers[i] is GravityProvider))
                    m_Providers[i].enabled = m_Enabled[i];
            // Existing real support grips remained selected throughout the approach. Rebase their
            // locomotion anchor to the new body position so the next climb step cannot undo the pull.
            if (climbProvider != null && climbProvider.climbAnchorInteractor != null && climbProvider.climbAnchorInteractable != null)
            {
                var anchor = climbProvider.climbAnchorInteractor;
                var hold = climbProvider.climbAnchorInteractable;
                climbProvider.FinishClimbGrab(anchor);
                climbProvider.StartClimbGrab(hold, anchor);
            }
            RemoveGravityLock();
            hand.FinishPull(attached, feedback);
            m_Providers.Clear(); m_Enabled.Clear();
        }

        protected override void OnDisable()
        {
            CancelPull("Movement component disabled");
            RemoveGravityLock();
            base.OnDisable();
        }

        public bool TryLockGravity(GravityOverride gravityOverride) => gravityProvider != null && gravityProvider.TryLockGravity(this, gravityOverride);
        public void RemoveGravityLock() { if (gravityProvider != null) gravityProvider.UnlockGravity(this); }
        public void OnGravityLockChanged(GravityOverride gravityOverride) { }
        public void OnGroundedChanged(bool isGrounded) { }
    }
}
