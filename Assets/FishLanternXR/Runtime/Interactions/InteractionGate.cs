using UnityEngine;

namespace FishLantern.XR
{
    /// <summary>
    /// Applies identity capabilities and the authoritative permission service before an interaction executes.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(InteractableIdentity))]
    public sealed class InteractionGate : MonoBehaviour, IInteractionResettable
    {
        [SerializeField, Min(0f)] private float denialFeedbackCooldown = 0.35f;
        [SerializeField] private bool requestTutorialOnDenial = true;
        [SerializeField] private InteractionEventUnityEvent interactionDenied = new();

        private InteractableIdentity identity;
        private float nextDenialFeedbackTime;

        /// <summary>Identity protected by this gate.</summary>
        public InteractableIdentity Identity => identity != null ? identity : identity = GetComponent<InteractableIdentity>();

        /// <summary>Raised when a denied request is reported after feedback throttling.</summary>
        public InteractionEventUnityEvent InteractionDenied => interactionDenied;

        /// <summary>Checks an interaction against local capabilities and the active authoritative permission service.</summary>
        public bool CanPerform(
            InteractionKind interactionKind,
            InteractionHand hand = InteractionHand.Unknown,
            string targetId = "",
            bool reportDenial = true)
        {
            var protectedIdentity = Identity;
            if (!protectedIdentity.Supports(interactionKind))
            {
                if (reportDenial)
                    Deny(interactionKind, InteractionFailureReason.WrongObject, hand, targetId);
                return false;
            }

            var hub = InteractionServiceHub.Active;
            if (hub == null || hub.CanInteract(protectedIdentity.CreateRequest(interactionKind, hand, targetId), out var reason))
                return true;

            if (reportDenial)
                Deny(interactionKind, reason, hand, targetId);
            return false;
        }

        /// <summary>Reports an explicit failed interaction and routes feedback without changing flow state.</summary>
        public void Deny(
            InteractionKind interactionKind,
            InteractionFailureReason reason,
            InteractionHand hand = InteractionHand.Unknown,
            string targetId = "",
            string details = "")
        {
            if (Time.unscaledTime < nextDenialFeedbackTime)
                return;

            nextDenialFeedbackTime = Time.unscaledTime + denialFeedbackCooldown;
            var protectedIdentity = Identity;
            var eventData = protectedIdentity.CreateEvent(
                interactionKind,
                InteractionResult.Failed,
                reason,
                targetId: targetId,
                hand: hand,
                details: details);

            var hub = InteractionServiceHub.Active;
            hub?.Report(eventData);
            hub?.FeedbackService?.PlayError(protectedIdentity, reason);
            if (requestTutorialOnDenial)
            {
                hub?.TutorialPromptService?.RequestPrompt(new TutorialPromptRequest(
                    $"interaction.error.{reason}",
                    protectedIdentity.StableId,
                    protectedIdentity.RequiredStep,
                    reason));
            }

            interactionDenied.Invoke(eventData);
        }

        /// <summary>Clears the denial-feedback throttle.</summary>
        public void ResetInteraction()
        {
            nextDenialFeedbackTime = 0f;
        }

        private void Awake()
        {
            identity = GetComponent<InteractableIdentity>();
        }
    }
}
