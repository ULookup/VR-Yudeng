using UnityEngine;

namespace FishLantern.XR
{
    /// <summary>Tracks whether a successful semantic event has occurred in the current run.</summary>
    [DisallowMultipleComponent]
    public sealed class EventPrerequisite : MonoBehaviour, IInteractionResettable
    {
        [SerializeField] private MonoBehaviour eventSource;
        [SerializeField] private MonoBehaviour stepSource;
        [SerializeField] private string requiredObjectId;
        [SerializeField] private InteractionKind requiredInteraction;
        [SerializeField] private FishLanternStep resetWhenEnteringStep = FishLanternStep.None;
        [SerializeField] private bool initiallySatisfied;

        private IInteractionEventStream stream;
        private IInteractionStepProvider stepProvider;

        /// <summary>Whether the configured prerequisite has been observed.</summary>
        public bool IsSatisfied { get; private set; }

        /// <summary>Configures the event and optional step sources.</summary>
        public void Configure(
            MonoBehaviour events,
            MonoBehaviour steps,
            string objectId,
            InteractionKind interaction,
            FishLanternStep resetStep,
            bool satisfiedAtStart = false)
        {
            Unsubscribe();
            eventSource = events;
            stepSource = steps;
            requiredObjectId = objectId ?? string.Empty;
            requiredInteraction = interaction;
            resetWhenEnteringStep = resetStep;
            initiallySatisfied = satisfiedAtStart;
            IsSatisfied = initiallySatisfied;
            Subscribe();
        }

        /// <summary>Restores the configured initial state.</summary>
        public void ResetInteraction()
        {
            IsSatisfied = initiallySatisfied;
        }

        private void Awake()
        {
            IsSatisfied = initiallySatisfied;
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
            if (eventData == null || eventData.Result != InteractionResult.Succeeded)
                return;
            if (eventData.InteractionKind != requiredInteraction)
                return;
            if (!string.IsNullOrWhiteSpace(requiredObjectId) && eventData.ObjectId != requiredObjectId)
                return;

            IsSatisfied = true;
        }

        private void HandleStepChanged(FishLanternStep step)
        {
            if (resetWhenEnteringStep == FishLanternStep.None || step == resetWhenEnteringStep)
                ResetInteraction();
        }
    }
}
