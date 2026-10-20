using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace FishLantern.XR
{
    /// <summary>Socket interactor that filters semantic identities and reports normalized socket events.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(InteractableIdentity))]
    public sealed class FishLanternSocketInteractor : XRSocketInteractor, IInteractionResettable
    {
        [SerializeField] private List<string> allowedObjectIds = new();
        [SerializeField] private List<InteractableObjectType> allowedObjectTypes = new();
        [SerializeField] private bool completeOnlyOnce = true;
        [SerializeField] private EventPrerequisite prerequisite;
        [SerializeField] private InteractionEventUnityEvent onSocketed = new();

        private InteractableIdentity socketIdentity;
        private bool completed;

        /// <summary>Raised after a compatible object is authoritatively accepted.</summary>
        public InteractionEventUnityEvent OnSocketed => onSocketed;

        /// <summary>Stable identifier reported as the event target.</summary>
        public string SocketId => socketIdentity != null ? socketIdentity.StableId : gameObject.name;

        /// <summary>Returns whether the candidate identity matches this socket's semantic filters.</summary>
        public bool Accepts(InteractableIdentity candidate)
        {
            return IsCompatible(candidate);
        }

        /// <summary>Assigns an optional semantic prerequisite for socket selection.</summary>
        public void SetPrerequisite(EventPrerequisite requiredEvent)
        {
            prerequisite = requiredEvent;
        }

        /// <summary>Configures accepted IDs and types from editor tooling.</summary>
        public void Configure(
            IEnumerable<string> objectIds,
            IEnumerable<InteractableObjectType> objectTypes,
            bool oneShot = true)
        {
            allowedObjectIds = objectIds == null ? new List<string>() : new List<string>(objectIds);
            allowedObjectTypes = objectTypes == null
                ? new List<InteractableObjectType>()
                : new List<InteractableObjectType>(objectTypes);
            completeOnlyOnce = oneShot;
        }

        /// <inheritdoc />
        public override bool CanHover(IXRHoverInteractable interactable)
        {
            return base.CanHover(interactable) && IsCompatible(interactable.transform);
        }

        /// <inheritdoc />
        public override bool CanSelect(IXRSelectInteractable interactable)
        {
            if (!base.CanSelect(interactable) || (completeOnlyOnce && completed && !IsSelecting(interactable)) ||
                (prerequisite != null && !prerequisite.IsSatisfied))
                return false;

            var candidate = FindIdentity(interactable.transform);
            if (candidate == null || !IsCompatible(candidate))
                return false;

            var gate = candidate.GetComponent<InteractionGate>();
            return gate == null || gate.CanPerform(
                InteractionKind.Socketed,
                InteractionHand.Unknown,
                SocketId,
                reportDenial: false);
        }

        /// <summary>Clears the one-shot completion latch.</summary>
        public void ResetInteraction()
        {
            completed = false;
        }

        /// <inheritdoc />
        protected override void Awake()
        {
            base.Awake();
            socketIdentity = GetComponent<InteractableIdentity>();
        }

        /// <inheritdoc />
        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            var candidate = FindIdentity(args.interactableObject.transform);
            if (candidate == null)
                return;

            completed = true;
            var eventData = candidate.CreateEvent(
                InteractionKind.Socketed,
                InteractionResult.Succeeded,
                progress: 1f,
                targetId: SocketId);
            var hub = InteractionServiceHub.Active;
            hub?.Report(eventData);
            hub?.FeedbackService?.PlaySuccess(candidate, InteractionKind.Socketed);
            onSocketed.Invoke(eventData);
        }

        private bool IsCompatible(Transform candidateTransform)
        {
            return IsCompatible(FindIdentity(candidateTransform));
        }

        private bool IsCompatible(InteractableIdentity candidate)
        {
            if (candidate == null)
                return false;

            var idAccepted = allowedObjectIds.Count == 0 || allowedObjectIds.Contains(candidate.StableId);
            var typeAccepted = allowedObjectTypes.Count == 0 || allowedObjectTypes.Contains(candidate.ObjectType);
            return idAccepted && typeAccepted;
        }

        private static InteractableIdentity FindIdentity(Transform candidateTransform)
        {
            return candidateTransform != null
                ? candidateTransform.GetComponentInParent<InteractableIdentity>()
                : null;
        }
    }
}
