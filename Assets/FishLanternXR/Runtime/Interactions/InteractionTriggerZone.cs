using System.Collections.Generic;
using UnityEngine;

namespace FishLantern.XR
{
    /// <summary>Reports a gated semantic action when a compatible identity enters a trigger collider.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class InteractionTriggerZone : MonoBehaviour, IInteractionResettable
    {
        [SerializeField] private string targetId;
        [SerializeField] private InteractionKind interactionKind = InteractionKind.TriggerEntered;
        [SerializeField] private List<string> allowedObjectIds = new();
        [SerializeField] private List<InteractableObjectType> allowedObjectTypes = new();
        [SerializeField] private bool oneShot = true;
        [SerializeField, Min(0f)] private float cooldownSeconds = 0.25f;
        [SerializeField] private InteractionEventUnityEvent onTriggered = new();

        private bool completed;
        private float nextAllowedTime;

        /// <summary>Raised when a compatible object passes permission checks.</summary>
        public InteractionEventUnityEvent OnTriggered => onTriggered;

        /// <summary>Configures the zone acceptance and normalized action.</summary>
        public void Configure(
            string id,
            InteractionKind kind,
            IEnumerable<string> objectIds,
            IEnumerable<InteractableObjectType> objectTypes,
            bool triggerOnce = true)
        {
            targetId = id ?? string.Empty;
            interactionKind = kind;
            allowedObjectIds = objectIds == null ? new List<string>() : new List<string>(objectIds);
            allowedObjectTypes = objectTypes == null
                ? new List<InteractableObjectType>()
                : new List<InteractableObjectType>(objectTypes);
            oneShot = triggerOnce;
        }

        /// <summary>Clears completion and cooldown.</summary>
        public void ResetInteraction()
        {
            completed = false;
            nextAllowedTime = 0f;
        }

        private void Reset()
        {
            var trigger = GetComponent<Collider>();
            trigger.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if ((oneShot && completed) || Time.unscaledTime < nextAllowedTime)
                return;

            var identity = other.GetComponentInParent<InteractableIdentity>();
            if (!IsCompatible(identity))
                return;

            var gate = identity.GetComponent<InteractionGate>();
            if (gate != null && !gate.CanPerform(interactionKind, targetId: targetId))
                return;

            nextAllowedTime = Time.unscaledTime + cooldownSeconds;
            completed = true;
            var eventData = identity.CreateEvent(
                interactionKind,
                InteractionResult.Succeeded,
                progress: 1f,
                targetId: targetId);
            var hub = InteractionServiceHub.Active;
            hub?.Report(eventData);
            hub?.FeedbackService?.PlaySuccess(identity, interactionKind);
            onTriggered.Invoke(eventData);
        }

        private bool IsCompatible(InteractableIdentity identity)
        {
            if (identity == null)
                return false;

            var idAccepted = allowedObjectIds.Count == 0 || allowedObjectIds.Contains(identity.StableId);
            var typeAccepted = allowedObjectTypes.Count == 0 || allowedObjectTypes.Contains(identity.ObjectType);
            return idAccepted && typeAccepted;
        }
    }
}
