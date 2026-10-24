using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Climbing;

namespace Levelstep
{
    public class ClimbStaminaController : MonoBehaviour, IXRSelectFilter
    {
        public enum HandSide { Left, Right }

        [Tooltip("Optional config asset. If unassigned, the serialized defaults below are used.")]
        public LevelstepClimbingConfig config;

        [Space]
        public float maxStamina = 100f;
        public float drainRate = 40f;
        public float regenRate = 45f;
        public float regenDelay = 0.6f;
        public float minStaminaToGrip = 20f;

        public static ClimbStaminaController Instance { get; private set; }

        public const string DifficultyPlayerPrefsKey = "Levelstep.Difficulty";
        public int DifficultyIndex { get; private set; }
        public event Action<HandSide> Exhausted;
        public event Action<HandSide> Recovered;

        /// <summary>While true (set by the balance system after a fall), hand stamina stays pinned at zero.</summary>
        public bool BalanceLock { get; private set; }

        public sealed class HandController
        {
            public float stamina;
            public bool gripping;
            public bool exhausted;
            public float regenWait;
            public readonly List<Grip> grips = new List<Grip>();
        }

        public struct Grip
        {
            public IXRSelectInteractor interactor;
            public IXRSelectInteractable interactable;

            public Grip(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
            {
                this.interactor = interactor;
                this.interactable = interactable;
            }
        }

        public readonly HandController Left = new HandController();
        public readonly HandController Right = new HandController();

        XRInteractionManager m_Manager;
        int m_ManagerRetryFrame;
        readonly HashSet<ClimbInteractable> m_Tracked = new HashSet<ClimbInteractable>();
        readonly Dictionary<object, HandSide> m_HandCache = new Dictionary<object, HandSide>();
        readonly Dictionary<HandSide, Transform> m_AnchorCache = new Dictionary<HandSide, Transform>();
        bool m_BarsCreated;

        void Awake()
        {
            Instance = this;
            SetDifficulty(config != null ? config.defaultDifficultyIndex : 1);
        }

        void Start()
        {
            if (config != null && config.rememberDifficulty && PlayerPrefs.HasKey(DifficultyPlayerPrefsKey))
                SetDifficulty(PlayerPrefs.GetInt(DifficultyPlayerPrefsKey, DifficultyIndex));

            AttemptManager();
            CreateHandBars();
        }

        void OnDestroy()
        {
            if (m_Manager != null)
            {
                m_Manager.selectFilters.Remove(this);
                m_Manager.interactableRegistered -= OnInteractableRegistered;
                m_Manager.interactableUnregistered -= OnInteractableUnregistered;
            }
            foreach (var climb in m_Tracked)
            {
                if (climb == null) continue;
                climb.selectEntered.RemoveListener(OnSelectEntered);
                climb.selectExited.RemoveListener(OnSelectExited);
            }
            m_Tracked.Clear();
            if (Instance == this) Instance = null;
        }

        public bool canProcess => isActiveAndEnabled;

        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
        {
            if (!(interactable is ClimbInteractable)) return true;
            var hand = GetHand(ResolveHandSide(interactor));
            return !BalanceLock && !hand.exhausted && hand.stamina > 0f;
        }

        void Update()
        {
            if (m_Manager == null && Time.frameCount >= m_ManagerRetryFrame)
                AttemptManager();

            TickHand(Left, HandSide.Left);
            TickHand(Right, HandSide.Right);
        }

        void TickHand(HandController hand, HandSide side)
        {
            if (hand.gripping)
            {
                hand.stamina -= DrainRate * Time.deltaTime;
                if (hand.stamina <= 0f)
                {
                    hand.stamina = 0f;
                    ExhaustHand(hand, side);
                }
            }
            else if (hand.regenWait > 0f)
            {
                hand.regenWait -= Time.deltaTime;
            }
            else if (BalanceLock)
            {
                hand.stamina = 0f;
                hand.exhausted = true;
            }
            else
            {
                hand.stamina = Mathf.Min(hand.stamina + RegenRate * Time.deltaTime, MaxStamina);
                if (hand.exhausted && hand.stamina >= MinStaminaToGrip)
                {
                    hand.exhausted = false;
                    Recovered?.Invoke(side);
                }
            }
        }

        void ExhaustHand(HandController hand, HandSide side)
        {
            hand.exhausted = true;
            for (int i = hand.grips.Count - 1; i >= 0; i--)
                ForceRelease(hand.grips[i].interactor, hand.grips[i].interactable);
            hand.grips.Clear();
            hand.gripping = false;
            hand.regenWait = RegenDelay;
            SendHapticPulse(side);
            Exhausted?.Invoke(side);
        }

        void ForceRelease(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
        {
            if (interactor == null || interactable == null || m_Manager == null) return;
            try
            {
                m_Manager.SelectExit(interactor, interactable);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Stamina] Force release failed: " + e.Message);
            }
        }

        /// <summary>
        /// Used by the balance board system: drains every hand to zero instantly so the built-in
        /// exhaustion path (force release, exhausted lock, regen delay, natural regen) plays out.
        /// No-op when balancing is absent - behaviour is otherwise unchanged.
        /// </summary>
        public void FallFromBalance()
        {
            ExhaustOrDrain(Left, HandSide.Left);
            ExhaustOrDrain(Right, HandSide.Right);
        }

        void ExhaustOrDrain(HandController hand, HandSide side)
        {
            hand.stamina = 0f;
            if (hand.gripping)
            {
                ExhaustHand(hand, side);
            }
            else
            {
                hand.exhausted = true;
                hand.regenWait = RegenDelay;
            }
        }

        /// <summary>Locks (or unlocks) stamina regeneration after a balance fall. While locked, hands stay
        /// empty and exhausted until the balance bar starts recovering.</summary>
        public void SetBalanceLock(bool locked) => BalanceLock = locked;

        void SendHapticPulse(HandSide side)
        {
            if (config == null || !config.hapticsEnabled) return;
            try
            {
                var controller = side == HandSide.Left ? HapticsUtility.Controller.Left : HapticsUtility.Controller.Right;
                HapticsUtility.SendHapticImpulse(config.hapticAmplitude, config.hapticDuration, controller);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Stamina] Haptic pulse failed: " + e.Message);
            }
        }

        void AttemptManager()
        {
            if (m_Manager != null) return;
            m_Manager = GetComponent<XRInteractionManager>() ?? UnityEngine.Object.FindAnyObjectByType<XRInteractionManager>();
            if (m_Manager == null)
            {
                m_ManagerRetryFrame = Time.frameCount + 30;
                return;
            }
            m_Manager.interactableRegistered += OnInteractableRegistered;
            m_Manager.interactableUnregistered += OnInteractableUnregistered;
            m_Manager.selectFilters.Add(this);
            RegisterExistingInteractables();
        }

        void RegisterExistingInteractables()
        {
            foreach (var climb in UnityEngine.Object.FindObjectsByType<ClimbInteractable>(FindObjectsInactive.Include))
                Track(climb);
        }

        void Track(ClimbInteractable climb)
        {
            if (!m_Tracked.Add(climb)) return;
            climb.selectEntered.AddListener(OnSelectEntered);
            climb.selectExited.AddListener(OnSelectExited);
        }

        void Untrack(ClimbInteractable climb)
        {
            if (!m_Tracked.Remove(climb)) return;
            climb.selectEntered.RemoveListener(OnSelectEntered);
            climb.selectExited.RemoveListener(OnSelectExited);
        }

        void OnInteractableRegistered(InteractableRegisteredEventArgs args)
        {
            if (args.interactableObject is ClimbInteractable climb)
                Track(climb);
        }

        void OnInteractableUnregistered(InteractableUnregisteredEventArgs args)
        {
            if (args.interactableObject is ClimbInteractable climb)
                Untrack(climb);
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            var interactor = args.interactorObject;
            var interactable = args.interactableObject;
            var side = ResolveHandSide(interactor);
            var hand = GetHand(side);

            // Stamina is checked by the select filter before XRI enters the grab. Releasing here
            // would let ClimbInteractable start locomotion after that nested release completes.
            hand.gripping = true;
            hand.regenWait = 0f;
            if (!ContainsGrip(hand, interactor, interactable))
                hand.grips.Add(new Grip(interactor, interactable));
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            var side = ResolveHandSide(args.interactorObject);
            var hand = GetHand(side);
            hand.grips.RemoveAll(g => g.interactor == args.interactorObject && g.interactable == args.interactableObject);
            if (hand.grips.Count == 0)
            {
                hand.gripping = false;
                hand.regenWait = RegenDelay;
            }
        }

        static bool ContainsGrip(HandController hand, IXRSelectInteractor interactor, IXRSelectInteractable interactable)
        {
            foreach (var grip in hand.grips)
                if (grip.interactor == interactor && grip.interactable == interactable)
                    return true;
            return false;
        }

        HandSide ResolveHandSide(IXRSelectInteractor interactor)
        {
            if (interactor != null && m_HandCache.TryGetValue(interactor, out var cached))
                return cached;

            HandSide side = GuessHandSide(interactor, HandSide.Left, config != null ? config.leftHandKeyword : "Left") ? HandSide.Left :
                            GuessHandSide(interactor, HandSide.Right, config != null ? config.rightHandKeyword : "Right") ? HandSide.Right :
                            HandSide.Left;

            if (interactor != null)
                m_HandCache[interactor] = side;
            return side;
        }

        static bool GuessHandSide(IXRSelectInteractor interactor, HandSide side, string keyword)
        {
            if (string.IsNullOrEmpty(keyword)) return false;
            Transform t = interactor != null ? interactor.transform : null;
            while (t != null)
            {
                if (t.name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                t = t.parent;
            }
            return false;
        }

        public HandController GetHand(HandSide side) => side == HandSide.Left ? Left : Right;

        public float GetStamina(HandSide side) => GetHand(side).stamina;

        public float GetNormalizedStamina(HandSide side) => Mathf.Clamp01(GetStamina(side) / MaxStamina);

        public bool IsGripping(HandSide side) => GetHand(side).gripping;

        public bool IsExhausted(HandSide side) => GetHand(side).exhausted;

        public float MaxStamina => maxStamina;
        public float DrainRate => drainRate;
        public float RegenRate => regenRate;
        public float RegenDelay => regenDelay;
        public float MinStaminaToGrip => minStaminaToGrip;

        public void SetDifficulty(int index)
        {
            if (config == null || config.difficultyPresets == null || config.difficultyPresets.Count == 0) return;
            index = Mathf.Clamp(index, 0, config.difficultyPresets.Count - 1);
            var preset = config.difficultyPresets[index];
            DifficultyIndex = index;
            maxStamina = preset.maxStamina;
            drainRate = preset.drainRate;
            regenRate = preset.regenRate;
            regenDelay = preset.regenDelay;
            minStaminaToGrip = preset.minStaminaToGrip;
            Left.stamina = Mathf.Min(Left.stamina, maxStamina);
            Right.stamina = Mathf.Min(Right.stamina, maxStamina);
        }

        public Transform FindHandAnchor(HandSide side)
        {
            if (m_AnchorCache.TryGetValue(side, out var cached) && cached != null)
                return cached;

            string keyword = side == HandSide.Left
                ? (config != null ? config.leftHandKeyword : "Left")
                : (config != null ? config.rightHandKeyword : "Right");
            if (string.IsNullOrEmpty(keyword))
                return null;

            Transform match = null;
            string anchorWord = side == HandSide.Left ? "Left" : "Right";
            var all = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);

            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (t.name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (IsExactAnchor(t, anchorWord))
                {
                    match = t;
                    break;
                }
            }

            if (match == null)
                for (int i = 0; i < all.Length; i++)
                {
                    Transform t = all[i];
                    if (t.name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (t.name.IndexOf(anchorWord, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (PicksKeyword(t, anchorWord))
                    {
                        match = t;
                        break;
                    }
                }

            if (match == null)
                for (int i = 0; i < all.Length; i++)
                {
                    Transform t = all[i];
                    if (t.name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        match = t;
                        break;
                    }
                }

            m_AnchorCache[side] = match;
            return match;
        }

        static bool IsExactAnchor(Transform t, string anchorWord)
        {
            return string.Equals(t.name, anchorWord + " Controller", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(t.name, anchorWord + " Hand", StringComparison.OrdinalIgnoreCase);
        }

        static bool PicksKeyword(Transform t, string anchorWord)
        {
            if (t.name.IndexOf(anchorWord + " Controller", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.name.IndexOf(anchorWord + " Hand", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.name.IndexOf(anchorWord, StringComparison.OrdinalIgnoreCase) >= 0 && t.parent != null &&
                t.parent.name.IndexOf("Camera Offset", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        void CreateHandBars()
        {
            if (m_BarsCreated) return;
            if (config == null || !config.showStaminaBars) return;
            m_BarsCreated = true;

            foreach (HandSide side in Enum.GetValues(typeof(HandSide)))
            {
                Transform anchor = FindHandAnchor(side);
                if (anchor == null)
                {
                    Debug.LogWarning("[Stamina] Could not find hand anchor for " + side + " (" +
                                     (side == HandSide.Left ? (config != null ? config.leftHandKeyword : "Left") : (config != null ? config.rightHandKeyword : "Right")) + ").");
                    continue;
                }
                var go = new GameObject("HandStaminaBar");
                go.transform.SetParent(anchor, false);
                var bar = go.AddComponent<HandStaminaBar>();
                bar.Initialize(this, side, config);
                Debug.Log("[Stamina] Hand bar created for " + side + " anchored to '" + anchor.name + "'.");
            }
        }
    }
}
