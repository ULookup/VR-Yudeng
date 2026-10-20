namespace FishLantern.XR
{
    /// <summary>Receives interaction lifecycle and completion events.</summary>
    public interface IInteractionEventReporter
    {
        /// <summary>Reports one normalized interaction event to the authoritative state layer.</summary>
        /// <param name="eventData">Complete interaction payload.</param>
        void Report(InteractionEventData eventData);
    }

    /// <summary>Read-only stream of normalized interaction events.</summary>
    public interface IInteractionEventStream
    {
        /// <summary>Raised whenever the state integration boundary receives an interaction event.</summary>
        InteractionEventUnityEvent InteractionReported { get; }
    }

    /// <summary>Read-only authoritative step source for views and prerequisite trackers.</summary>
    public interface IInteractionStepProvider
    {
        /// <summary>Current authoritative fish-lantern step.</summary>
        FishLanternStep CurrentStep { get; }

        /// <summary>Raised after the authoritative step changes.</summary>
        FishLanternStepUnityEvent StepChanged { get; }
    }

    /// <summary>Authoritatively decides whether an interaction may occur.</summary>
    public interface IInteractionPermissionService
    {
        /// <summary>Checks a request without changing game state.</summary>
        /// <param name="request">Requested operation and source identity.</param>
        /// <param name="failureReason">Reason returned when permission is denied.</param>
        /// <returns><c>true</c> when the interaction may proceed.</returns>
        bool CanInteract(InteractionRequest request, out InteractionFailureReason failureReason);
    }

    /// <summary>Centralized visual, audio and haptic feedback entry point.</summary>
    public interface IInteractionFeedbackService
    {
        /// <summary>Sets persistent eligible-object highlighting.</summary>
        void SetHighlight(InteractableIdentity identity, bool enabled);

        /// <summary>Plays positive feedback for a completed interaction.</summary>
        void PlaySuccess(InteractableIdentity identity, InteractionKind interactionKind);

        /// <summary>Plays error feedback and exposes the machine-readable reason.</summary>
        void PlayError(InteractableIdentity identity, InteractionFailureReason failureReason);

        /// <summary>Requests controller vibration without coupling interactions to a device API.</summary>
        void Pulse(InteractionHand hand, float amplitude, float durationSeconds);
    }

    /// <summary>Requests tutorial presentation without owning tutorial content.</summary>
    public interface ITutorialPromptService
    {
        /// <summary>Forwards a semantic tutorial prompt request.</summary>
        void RequestPrompt(TutorialPromptRequest request);
    }

    /// <summary>Implemented by interactions that support authoritative reset.</summary>
    public interface IInteractionResettable
    {
        /// <summary>Restores the component to its initial interaction state.</summary>
        void ResetInteraction();
    }
}
