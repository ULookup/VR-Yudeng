using System.Collections.Generic;
using UnityEngine;

namespace FishLantern.XR
{
    /// <summary>Tracks ordered or unordered tool visits across a low-cost drawing path.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(InteractableIdentity), typeof(InteractionGate))]
    public sealed class PathCheckpointInteraction : MonoBehaviour, IInteractionResettable
    {
        [SerializeField] private List<InteractableObjectType> acceptedToolTypes = new();
        [SerializeField] private InteractionKind progressKind = InteractionKind.SketchProgress;
        [SerializeField] private InteractionKind completionKind = InteractionKind.SketchComplete;
        [SerializeField, Min(1)] private int checkpointCount = 5;
        [SerializeField] private bool requireOrderedVisits = true;
        [SerializeField] private bool requireTriggerActivation;
        [SerializeField] private EventPrerequisite prerequisite;
        [SerializeField] private InteractionEventUnityEvent onProgress = new();
        [SerializeField] private InteractionEventUnityEvent onCompleted = new();

        private readonly HashSet<int> visited = new();
        private InteractableIdentity identity;
        private InteractionGate gate;
        private bool completed;

        /// <summary>Current normalized checkpoint coverage.</summary>
        public float Progress => completed ? 1f : visited.Count / (float)Mathf.Max(1, checkpointCount);

        /// <summary>The next ordered checkpoint, or the checkpoint count after completion.</summary>
        public int NextCheckpointIndex => visited.Count;

        public bool IsComplete => completed;

        /// <summary>Raised after a valid new checkpoint.</summary>
        public InteractionEventUnityEvent OnProgress => onProgress;

        /// <summary>Raised once after every checkpoint is visited.</summary>
        public InteractionEventUnityEvent OnCompleted => onCompleted;

        /// <summary>Requires the held tool's existing XRI activation input during a visit.</summary>
        public void ConfigureActivation(bool required) => requireTriggerActivation = required;

        /// <summary>Configures tool filtering, event types and checkpoint policy.</summary>
        public void Configure(
            IEnumerable<InteractableObjectType> toolTypes,
            InteractionKind progressEvent,
            InteractionKind completionEvent,
            int totalCheckpoints,
            bool ordered,
            EventPrerequisite requiredEvent = null)
        {
            acceptedToolTypes = toolTypes == null
                ? new List<InteractableObjectType>()
                : new List<InteractableObjectType>(toolTypes);
            progressKind = progressEvent;
            completionKind = completionEvent;
            checkpointCount = Mathf.Max(1, totalCheckpoints);
            requireOrderedVisits = ordered;
            prerequisite = requiredEvent;
        }

        /// <summary>Attempts to register a tool visit at a checkpoint index.</summary>
        public bool TryVisit(int checkpointIndex, InteractableIdentity tool)
        {
            EnsureReferences();
            if (completed || checkpointIndex < 0 || checkpointIndex >= checkpointCount)
                return false;
            if (prerequisite != null && !prerequisite.IsSatisfied)
            {
                gate.Deny(progressKind, InteractionFailureReason.PrerequisiteMissing);
                return false;
            }
            if (tool == null || (acceptedToolTypes.Count > 0 && !acceptedToolTypes.Contains(tool.ObjectType)))
            {
                gate.Deny(progressKind, InteractionFailureReason.WrongObject);
                return false;
            }
            var heldTool = tool.GetComponent<GrabInteractableEx>();
            if (heldTool == null || !heldTool.IsHeldByHand ||
                (requireTriggerActivation && !heldTool.TriggerPressed))
                return false;
            if (visited.Contains(checkpointIndex))
                return false;
            if (requireOrderedVisits && checkpointIndex != visited.Count)
            {
                gate.Deny(progressKind, InteractionFailureReason.Misaligned);
                return false;
            }
            if (!gate.CanPerform(progressKind) || !visited.Add(checkpointIndex))
                return false;

            var progressEvent = identity.CreateEvent(
                progressKind,
                InteractionResult.Progressed,
                progress: Progress,
                targetId: checkpointIndex.ToString());
            InteractionServiceHub.Active?.Report(progressEvent);
            onProgress.Invoke(progressEvent);

            if (visited.Count >= checkpointCount)
                Complete();
            return true;
        }

        /// <summary>Clears all visited checkpoints.</summary>
        public void ResetInteraction()
        {
            visited.Clear();
            completed = false;
        }

        private void Awake()
        {
            EnsureReferences();
        }

        private void Complete()
        {
            completed = true;
            var eventData = identity.CreateEvent(
                completionKind,
                InteractionResult.Succeeded,
                progress: 1f);
            var hub = InteractionServiceHub.Active;
            hub?.Report(eventData);
            hub?.FeedbackService?.PlaySuccess(identity, completionKind);
            onCompleted.Invoke(eventData);
        }

        private void EnsureReferences()
        {
            identity ??= GetComponent<InteractableIdentity>();
            gate ??= GetComponent<InteractionGate>();
        }
    }

}
