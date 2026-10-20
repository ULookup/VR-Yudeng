using UnityEngine;

namespace FishLantern.XR
{
    /// <summary>
    /// Production integration boundary. The real GameStateManager owns the step and subscribes to reported events.
    /// </summary>
    public sealed class GameStateManagerAdapter : MonoBehaviour,
        IInteractionEventReporter,
        IInteractionPermissionService,
        IInteractionEventStream,
        IInteractionStepProvider,
        ITutorialPromptService
    {
        [SerializeField] private FishLanternStep authoritativeStep = FishLanternStep.Welcome;
        [SerializeField] private MonoBehaviour permissionPolicy;
        [SerializeField] private InteractionEventUnityEvent interactionReported = new();
        [SerializeField] private FishLanternStepUnityEvent authoritativeStepChanged = new();
        [SerializeField] private TutorialPromptUnityEvent tutorialPromptRequested = new();

        /// <summary>Step last supplied by the production state manager.</summary>
        public FishLanternStep AuthoritativeStep => authoritativeStep;

        /// <summary>Current step supplied by the production state manager.</summary>
        public FishLanternStep CurrentStep => authoritativeStep;

        /// <summary>Event for the production state manager to consume.</summary>
        public InteractionEventUnityEvent InteractionReported => interactionReported;

        /// <summary>Raised after the production state manager supplies a different step.</summary>
        public FishLanternStepUnityEvent AuthoritativeStepChanged => authoritativeStepChanged;

        /// <summary>Read-only step change stream used by project views and prerequisite trackers.</summary>
        public FishLanternStepUnityEvent StepChanged => authoritativeStepChanged;

        /// <summary>Event for the production tutorial system to consume.</summary>
        public TutorialPromptUnityEvent TutorialPromptRequested => tutorialPromptRequested;

        /// <summary>Reports normalized data without advancing state locally.</summary>
        public void Report(InteractionEventData eventData)
        {
            if (eventData != null)
                interactionReported.Invoke(eventData);
        }

        /// <summary>Allows interactions only for the step supplied by the production state manager.</summary>
        public bool CanInteract(
            InteractionRequest request,
            out InteractionFailureReason failureReason)
        {
            // Keep the rod's original event stage. The production policy must explicitly allow
            // this particular rod and operation when viewing the completed work.
            var completedRodDisplay = authoritativeStep == FishLanternStep.Complete &&
                request.RequiredStep == FishLanternStep.LampDisplay &&
                request.ObjectType == InteractableObjectType.WoodenRod &&
                permissionPolicy is IInteractionPermissionService;
            if (request.RequiredStep != FishLanternStep.None &&
                request.RequiredStep != authoritativeStep && !completedRodDisplay)
            {
                failureReason = InteractionFailureReason.WrongStep;
                return false;
            }

            failureReason = InteractionFailureReason.None;
            return permissionPolicy is not IInteractionPermissionService policy ||
                   policy.CanInteract(request, out failureReason);
        }

        /// <summary>Sets the production rules that supplement the current-step restriction.</summary>
        public void ConfigurePermissionPolicy(MonoBehaviour policy)
        {
            permissionPolicy = policy != this ? policy : null;
        }

        /// <summary>Forwards tutorial prompt requests to the production tutorial system.</summary>
        public void RequestPrompt(TutorialPromptRequest request)
        {
            tutorialPromptRequested.Invoke(request);
        }

        /// <summary>
        /// Called only by the real GameStateManager when its authoritative step changes.
        /// Interaction scripts must never call this method to advance flow.
        /// </summary>
        public void SetAuthoritativeStep(FishLanternStep step)
        {
            if (authoritativeStep == step)
                return;

            authoritativeStep = step;
            authoritativeStepChanged.Invoke(authoritativeStep);
        }
    }
}
