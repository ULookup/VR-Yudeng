using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishLantern.XR
{
    /// <summary>
    /// Stable identity and semantic metadata required on every fish-lantern interactable.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InteractableIdentity : MonoBehaviour
    {
        [SerializeField] private string stableId;
        [SerializeField] private string displayName;
        [SerializeField] private InteractableObjectType objectType = InteractableObjectType.Unknown;
        [SerializeField] private FishLanternStep requiredStep = FishLanternStep.None;
        [SerializeField] private List<InteractionKind> allowedInteractions = new();

        /// <summary>Stable ID used by saves, events, sockets and the state manager.</summary>
        public string StableId => stableId;

        /// <summary>Human-readable object name for diagnostics.</summary>
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? gameObject.name : displayName;

        /// <summary>Semantic object type.</summary>
        public InteractableObjectType ObjectType => objectType;

        /// <summary>Step during which this object is normally allowed.</summary>
        public FishLanternStep RequiredStep => requiredStep;

        /// <summary>Returns whether the object declares support for an interaction kind.</summary>
        public bool Supports(InteractionKind interactionKind)
        {
            return allowedInteractions.Count == 0 || allowedInteractions.Contains(interactionKind);
        }

        /// <summary>Creates a permission request using this identity.</summary>
        public InteractionRequest CreateRequest(
            InteractionKind interactionKind,
            InteractionHand hand = InteractionHand.Unknown,
            string targetId = "")
        {
            return new InteractionRequest(
                stableId,
                objectType,
                requiredStep,
                interactionKind,
                hand,
                targetId);
        }

        /// <summary>Creates a normalized event payload using this identity.</summary>
        public InteractionEventData CreateEvent(
            InteractionKind interactionKind,
            InteractionResult result,
            InteractionFailureReason failureReason = InteractionFailureReason.None,
            float progress = 0f,
            string targetId = "",
            InteractionHand hand = InteractionHand.Unknown,
            int attempt = 0,
            string details = "")
        {
            return new InteractionEventData(
                stableId,
                objectType,
                requiredStep,
                interactionKind,
                result,
                failureReason,
                progress,
                targetId,
                hand,
                transform.position,
                attempt,
                details);
        }

        /// <summary>Assigns deterministic metadata from editor tooling.</summary>
        public void Configure(
            string id,
            string label,
            InteractableObjectType type,
            FishLanternStep step,
            IEnumerable<InteractionKind> interactions)
        {
            stableId = id ?? string.Empty;
            displayName = label ?? string.Empty;
            objectType = type;
            requiredStep = step;
            allowedInteractions = interactions == null
                ? new List<InteractionKind>()
                : new List<InteractionKind>(interactions);
        }

        private void Reset()
        {
            EnsureId();
            displayName = gameObject.name;
        }

        private void OnValidate()
        {
            EnsureId();
        }

        private void EnsureId()
        {
            if (string.IsNullOrWhiteSpace(stableId))
                stableId = Guid.NewGuid().ToString("N");
        }
    }
}
