using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FishLantern.XR
{
    /// <summary>One event requirement used by <see cref="StageProgressController"/>.</summary>
    [Serializable]
    public sealed class StageEventRequirement
    {
        [SerializeField] private string objectId;
        [SerializeField] private InteractionKind interactionKind;
        [SerializeField] private string targetId;
        [SerializeField, Min(1)] private int requiredCount = 1;
        [SerializeField] private bool optionalInEasyMode;

        /// <summary>Required source object ID; empty matches any object.</summary>
        public string ObjectId => objectId;

        /// <summary>Required successful interaction kind.</summary>
        public InteractionKind InteractionKind => interactionKind;

        /// <summary>Optional required target ID.</summary>
        public string TargetId => targetId;

        /// <summary>Number of matching successful events required.</summary>
        public int RequiredCount => Mathf.Max(1, requiredCount);

        /// <summary>Whether easy mode omits this requirement.</summary>
        public bool OptionalInEasyMode => optionalInEasyMode;

        /// <summary>Creates a serializable stage event requirement.</summary>
        public StageEventRequirement(
            string sourceObjectId,
            InteractionKind kind,
            string requiredTargetId = "",
            int count = 1,
            bool easyModeOptional = false)
        {
            objectId = sourceObjectId ?? string.Empty;
            interactionKind = kind;
            targetId = requiredTargetId ?? string.Empty;
            requiredCount = Mathf.Max(1, count);
            optionalInEasyMode = easyModeOptional;
        }

        /// <summary>Returns whether a successful event matches this requirement.</summary>
        public bool Matches(InteractionEventData eventData)
        {
            return eventData != null &&
                   eventData.Result == InteractionResult.Succeeded &&
                   eventData.InteractionKind == interactionKind &&
                   (string.IsNullOrWhiteSpace(objectId) || eventData.ObjectId == objectId) &&
                   (string.IsNullOrWhiteSpace(targetId) || eventData.TargetId == targetId);
        }
    }

    /// <summary>
    /// Aggregates successful interaction events and reports StepComplete without changing authoritative state.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(InteractableIdentity), typeof(InteractionGate))]
    public sealed class StageProgressController : MonoBehaviour, IInteractionResettable
    {
        [SerializeField] private MonoBehaviour eventSource;
        [SerializeField] private MonoBehaviour stepSource;
        [SerializeField] private FishLanternStep stage;
        [SerializeField] private bool easyMode;
        [SerializeField] private List<StageEventRequirement> requirements = new();
        [SerializeField] private InteractionEventUnityEvent onStageCompleted = new();

        private readonly Dictionary<StageEventRequirement, int> counts = new();
        private IInteractionEventStream stream;
        private IInteractionStepProvider stepProvider;
        private InteractableIdentity identity;
        private InteractionGate gate;
        private bool completionReported;

        /// <summary>Raised after all active requirements are satisfied.</summary>
        public InteractionEventUnityEvent OnStageCompleted => onStageCompleted;

        /// <summary>Step aggregated by this controller.</summary>
        public FishLanternStep Stage => stage;

        /// <summary>Number of serialized completion requirements.</summary>
        public int RequirementCount => requirements.Count;

        /// <summary>True only after this stage has observed every required successful action.</summary>
        public bool IsComplete => completionReported;

        /// <summary>Normalized completion from zero to one.</summary>
        public float Progress
        {
            get
            {
                var activeCount = 0;
                var accumulated = 0f;
                foreach (var requirement in requirements)
                {
                    if (easyMode && requirement.OptionalInEasyMode) continue;
                    activeCount++;
                    accumulated += Mathf.Clamp01(GetCount(requirement) / (float)requirement.RequiredCount);
                }
                return activeCount == 0 ? 0f : accumulated / activeCount;
            }
        }

        /// <summary>Configures sources, stage, difficulty and completion requirements.</summary>
        public void Configure(
            MonoBehaviour events,
            MonoBehaviour steps,
            FishLanternStep controlledStage,
            IEnumerable<StageEventRequirement> eventRequirements,
            bool useEasyMode = false)
        {
            Unsubscribe();
            eventSource = events;
            stepSource = steps;
            stage = controlledStage;
            easyMode = useEasyMode;
            requirements = eventRequirements == null
                ? new List<StageEventRequirement>()
                : new List<StageEventRequirement>(eventRequirements);
            EnsureReferences();
            ResetInteraction();
            Subscribe();
        }

        /// <summary>Clears all requirement counts and re-arms step completion.</summary>
        public void ResetInteraction()
        {
            counts.Clear();
            foreach (var requirement in requirements)
                counts[requirement] = 0;
            completionReported = false;
        }

        private IEnumerable<StageEventRequirement> ActiveRequirements =>
            requirements.Where(requirement => !(easyMode && requirement.OptionalInEasyMode));

        private void Awake()
        {
            EnsureReferences();
            ResetInteraction();
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            Unsubscribe();
            stream = eventSource as IInteractionEventStream;
            stepProvider = stepSource as IInteractionStepProvider;
            stream?.InteractionReported.AddListener(HandleEvent);
            stepProvider?.StepChanged.AddListener(HandleStepChanged);
        }

        private void Unsubscribe()
        {
            stream?.InteractionReported.RemoveListener(HandleEvent);
            stepProvider?.StepChanged.RemoveListener(HandleStepChanged);
            stream = null;
            stepProvider = null;
        }

        private void HandleEvent(InteractionEventData eventData)
        {
            if (completionReported || eventData == null || eventData.Step != stage ||
                stepProvider == null || stepProvider.CurrentStep != stage)
                return;

            foreach (var requirement in ActiveRequirements)
            {
                if (!requirement.Matches(eventData))
                    continue;

                counts[requirement] = Mathf.Min(
                    GetCount(requirement) + 1,
                    requirement.RequiredCount);
            }

            TryComplete();
        }

        private void TryComplete()
        {
            if (completionReported || !ActiveRequirements.Any() || ActiveRequirements.Any(requirement =>
                    GetCount(requirement) < requirement.RequiredCount))
            {
                return;
            }

            EnsureReferences();
            if (!gate.CanPerform(InteractionKind.StepComplete))
                return;

            completionReported = true;
            var completion = identity.CreateEvent(
                InteractionKind.StepComplete,
                InteractionResult.Succeeded,
                progress: 1f,
                details: $"All required actions for {stage} completed.");
            InteractionServiceHub.Active?.Report(completion);
            onStageCompleted.Invoke(completion);
        }

        private void HandleStepChanged(FishLanternStep current)
        {
            if (current == stage)
                ResetInteraction();
        }

        private int GetCount(StageEventRequirement requirement)
        {
            return counts.TryGetValue(requirement, out var value) ? value : 0;
        }

        private void EnsureReferences()
        {
            identity ??= GetComponent<InteractableIdentity>();
            gate ??= GetComponent<InteractionGate>();
        }
    }
}
