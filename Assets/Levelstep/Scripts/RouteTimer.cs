using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Climbing;

namespace Levelstep
{
    /// <summary>
    /// Stopwatch for a climbing run. The timer starts when the player grabs the Start hold of the
    /// currently selected difficulty route and stops when they grab that route's Finish hold
    /// (success) or when a balance fall fires (aborted). Grabbing the Start hold again after a run
    /// has ended restarts it. Displays through the headset HUD (see BalanceBoardUI).
    /// </summary>
    public class RouteTimer : MonoBehaviour
    {
        public enum State
        {
            Idle = 0,
            Running = 1,
            Finished = 2,
            Aborted = 3
        }

        public static RouteTimer Instance { get; private set; }

        /// <summary>Current state of the run.</summary>
        public State CurrentState { get; private set; } = State.Idle;

        /// <summary>Elapsed seconds of the current run (only meaningful while Running).</summary>
        public float Elapsed { get; private set; }

        /// <summary>Final elapsed seconds of the last ended run.</summary>
        public float LastResult { get; private set; }

        /// <summary>Time.time when the last run ended (used by the HUD to show the result briefly).</summary>
        public float LastEndTime { get; private set; } = -1000f;

        /// <summary>Whether the last ended run was finished (true) or aborted by a fall (false).</summary>
        public bool LastSuccess { get; private set; }

        /// <summary>Seconds the player may hold nothing before a running route is aborted (they let go).</summary>
        public float letGoGraceSeconds = 0.4f;

        /// <summary>Raised when a run ends. Arguments: elapsed seconds, success.</summary>
        public event Action<float, bool> RunEnded;
        /// <summary>Local run sequence, not a mobile-app attempt identifier.</summary>
        public int RunSequence { get; private set; }
        public event Action RunStarted;

        XRInteractionManager m_Manager;
        readonly HashSet<ClimbInteractable> m_Tracked = new HashSet<ClimbInteractable>();
        readonly HashSet<(IXRSelectInteractor interactor, IXRSelectInteractable interactable)> m_Grips =
            new HashSet<(IXRSelectInteractor, IXRSelectInteractable)>();
        BalanceMeterController m_Meter;
        bool m_TrackingFell;
        float m_NoGripTimer;

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (m_TrackingFell && m_Meter != null)
                m_Meter.FellOff -= OnFell;
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            AttemptManager();
        }

        void Update()
        {
            AttemptManager();
            if (!m_TrackingFell && BalanceMeterController.Instance != null)
            {
                m_Meter = BalanceMeterController.Instance;
                m_Meter.FellOff += OnFell;
                m_TrackingFell = true;
            }
            if (CurrentState == State.Running)
            {
                Elapsed += Time.deltaTime;

                if (m_Grips.Count == 0)
                {
                    m_NoGripTimer += Time.deltaTime;
                    if (m_NoGripTimer >= letGoGraceSeconds)
                        FinishRun(false, "let go");
                }
                else
                {
                    m_NoGripTimer = 0f;
                }
            }
        }

        int CurrentRouteIndex()
        {
            if (DifficultyRouteController.Instance != null)
                return DifficultyRouteController.Instance.SelectedIndex;
            if (ClimbStaminaController.Instance != null)
                return ClimbStaminaController.Instance.DifficultyIndex;
            return 0;
        }

        void AttemptManager()
        {
            if (m_Manager != null) return;
            m_Manager = GetComponent<XRInteractionManager>() ?? UnityEngine.Object.FindAnyObjectByType<XRInteractionManager>();
            if (m_Manager == null) return;
            m_Manager.interactableRegistered += OnInteractableRegistered;
            m_Manager.interactableUnregistered += OnInteractableUnregistered;
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
            m_Grips.Add((args.interactorObject, args.interactableObject));
            m_NoGripTimer = 0f;

            var hold = args.interactableObject.transform.GetComponentInParent<LevelstepHold>();
            if (hold == null || hold.role == RouteRole.Normal) return;

            var tag = hold.GetComponentInParent<HoldRouteTag>();
            if (tag == null || (int)tag.routeType != CurrentRouteIndex()) return;

            if (hold.role == RouteRole.Start && CurrentState != State.Running)
            {
                Elapsed = 0f;
                CurrentState = State.Running;
                m_NoGripTimer = 0f;
                RunSequence++;
                RunStarted?.Invoke();
                Debug.Log("[RouteTimer] Run started on Start hold '" + hold.name + "'.");
            }
            else if (hold.role == RouteRole.Finish && CurrentState == State.Running)
            {
                FinishRun(true);
            }
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            m_Grips.Remove((args.interactorObject, args.interactableObject));
        }

        void OnFell()
        {
            if (CurrentState == State.Running)
                FinishRun(false, "balance fall");
        }

        void FinishRun(bool success, string cause = null)
        {
            CurrentState = success ? State.Finished : State.Aborted;
            LastResult = Elapsed;
            LastEndTime = Time.time;
            LastSuccess = success;
            Debug.Log("[RouteTimer] Run " + (success ? "finished" : "aborted (" + (cause ?? "fall") + ")") + " at " + Elapsed.ToString("F2") + "s.");
            RunEnded?.Invoke(Elapsed, success);
        }
    }
}
