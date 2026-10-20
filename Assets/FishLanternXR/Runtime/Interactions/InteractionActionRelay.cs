using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace FishLantern.XR
{
    /// <summary>
    /// Unifies UI clicks, XR activation and XR selection into one gated semantic action.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(InteractableIdentity), typeof(InteractionGate))]
    public sealed class InteractionActionRelay : MonoBehaviour, IInteractionResettable
    {
        [SerializeField] private XRBaseInteractable xrSource;
        [SerializeField] private InteractionKind actionKind = InteractionKind.ButtonPressed;
        [SerializeField] private bool listenToActivate = true;
        [SerializeField] private bool listenToSelectEntered;
        [SerializeField] private bool oneShot;
        [SerializeField, Min(0f)] private float cooldownSeconds = 0.35f;
        [SerializeField] private bool interactionEnabled = true;
        [SerializeField] private EventPrerequisite prerequisite;
        [SerializeField] private InteractionEventUnityEvent onSucceeded = new();
        [SerializeField] private InteractionEventUnityEvent onFailed = new();

        private InteractableIdentity identity;
        private InteractionGate gate;
        private bool completed;
        private float nextAllowedTime;

        /// <summary>Raised after the semantic action succeeds.</summary>
        public InteractionEventUnityEvent OnSucceeded => onSucceeded;

        /// <summary>Raised when disabled, cooling down, completed or denied by the state service.</summary>
        public InteractionEventUnityEvent OnFailed => onFailed;

        /// <summary>Whether this relay has succeeded at least once in the current run.</summary>
        public bool IsCompleted => completed;

        /// <summary>Semantic action produced by this relay.</summary>
        public InteractionKind ActionKind => actionKind;

        /// <summary>Whether input is currently accepted.</summary>
        public bool InteractionEnabled
        {
            get => interactionEnabled;
            set => interactionEnabled = value;
        }

        /// <summary>Configures the normalized action and repeat policy from editor bootstrap code.</summary>
        public void Configure(InteractionKind kind, bool triggerOnce, float cooldown)
        {
            actionKind = kind;
            oneShot = triggerOnce;
            cooldownSeconds = Mathf.Max(0f, cooldown);
        }

        /// <summary>Assigns an optional semantic prerequisite.</summary>
        public void SetPrerequisite(EventPrerequisite requiredEvent)
        {
            prerequisite = requiredEvent;
        }

        /// <summary>Chooses which XR Interaction Toolkit events feed this relay.</summary>
        public void ConfigureXrInputs(bool activate, bool selectEntered)
        {
            listenToActivate = activate;
            listenToSelectEntered = selectEntered;
        }

        /// <summary>Entry point for a Unity UI Button OnClick event.</summary>
        public void Press()
        {
            TryInvoke(InteractionHand.Unknown);
        }

        /// <summary>Attempts the configured semantic action for a known hand.</summary>
        public bool TryInvoke(InteractionHand hand)
        {
            EnsureReferences();
            // Installation and switching on require an explicit, active player operation.
            if ((actionKind == InteractionKind.LampInstalled || actionKind == InteractionKind.LightOn) &&
                (!isActiveAndEnabled || !Application.isFocused))
                return Fail(InteractionFailureReason.Cancelled, hand);
            if (!interactionEnabled)
                return Fail(InteractionFailureReason.PrerequisiteMissing, hand);
            if (prerequisite != null && !prerequisite.IsSatisfied)
                return Fail(InteractionFailureReason.PrerequisiteMissing, hand);
            if (oneShot && completed)
                return Fail(InteractionFailureReason.AlreadyCompleted, hand);
            if (Time.unscaledTime < nextAllowedTime)
                return Fail(InteractionFailureReason.Cooldown, hand);
            if (!gate.CanPerform(actionKind, hand))
                return false;

            nextAllowedTime = Time.unscaledTime + cooldownSeconds;
            completed = true;
            var eventData = identity.CreateEvent(
                actionKind,
                InteractionResult.Succeeded,
                progress: 1f,
                hand: hand);
            var hub = InteractionServiceHub.Active;
            hub?.Report(eventData);
            hub?.FeedbackService?.PlaySuccess(identity, actionKind);
            onSucceeded.Invoke(eventData);
            return true;
        }

        /// <summary>Clears one-shot completion and cooldown.</summary>
        public void ResetInteraction()
        {
            completed = false;
            nextAllowedTime = 0f;
        }

        private void Awake()
        {
            EnsureReferences();
            if (xrSource == null)
                xrSource = GetComponent<XRBaseInteractable>();
        }

        private void OnEnable()
        {
            if (xrSource == null)
                xrSource = GetComponent<XRBaseInteractable>();
            if (xrSource == null)
                return;

            if (listenToActivate)
                xrSource.activated.AddListener(HandleActivated);
            if (listenToSelectEntered)
                xrSource.selectEntered.AddListener(HandleSelected);
        }

        private void OnDisable()
        {
            if (xrSource == null)
                return;

            xrSource.activated.RemoveListener(HandleActivated);
            xrSource.selectEntered.RemoveListener(HandleSelected);
        }

        private void HandleActivated(ActivateEventArgs args)
        {
            TryInvoke(InteractionHandResolver.Resolve(args.interactorObject));
        }

        private void HandleSelected(SelectEnterEventArgs args)
        {
            TryInvoke(InteractionHandResolver.Resolve(args.interactorObject));
        }

        private bool Fail(InteractionFailureReason reason, InteractionHand hand)
        {
            var eventData = identity.CreateEvent(
                actionKind,
                InteractionResult.Failed,
                reason,
                hand: hand);
            InteractionServiceHub.Active?.FeedbackService?.PlayError(identity, reason);
            onFailed.Invoke(eventData);
            return false;
        }

        private void EnsureReferences()
        {
            identity ??= GetComponent<InteractableIdentity>();
            gate ??= GetComponent<InteractionGate>();
        }
    }
}
