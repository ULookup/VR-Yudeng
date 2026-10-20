using UnityEngine;

namespace FishLantern.XR
{
    /// <summary>
    /// Scene-level dependency boundary used by interactions to reach state, feedback and tutorial services.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class InteractionServiceHub : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour eventReporter;
        [SerializeField] private MonoBehaviour permissionService;
        [SerializeField] private MonoBehaviour feedbackService;
        [SerializeField] private MonoBehaviour tutorialPromptService;

        /// <summary>Currently active scene service hub.</summary>
        public static InteractionServiceHub Active { get; private set; }

        /// <summary>Configured interaction event reporter, or <c>null</c>.</summary>
        public IInteractionEventReporter EventReporter => eventReporter as IInteractionEventReporter;

        /// <summary>Configured permission service, or <c>null</c>.</summary>
        public IInteractionPermissionService PermissionService => permissionService as IInteractionPermissionService;

        /// <summary>Configured feedback service, or <c>null</c>.</summary>
        public IInteractionFeedbackService FeedbackService => feedbackService as IInteractionFeedbackService;

        /// <summary>Configured tutorial prompt service, or <c>null</c>.</summary>
        public ITutorialPromptService TutorialPromptService => tutorialPromptService as ITutorialPromptService;

        /// <summary>Assigns scene service providers. Primarily used by editor bootstrap and tests.</summary>
        public void Configure(
            MonoBehaviour reporter,
            MonoBehaviour permissions,
            MonoBehaviour feedback,
            MonoBehaviour tutorial)
        {
            eventReporter = reporter;
            permissionService = permissions;
            feedbackService = feedback;
            tutorialPromptService = tutorial;
            ValidateProviders();
        }

        /// <summary>Reports an event when a reporter is available.</summary>
        public void Report(InteractionEventData eventData)
        {
            EventReporter?.Report(eventData);
        }

        /// <summary>Checks authoritative permission; missing permission services default to allowed for edit isolation.</summary>
        public bool CanInteract(
            InteractionRequest request,
            out InteractionFailureReason failureReason)
        {
            if (PermissionService == null)
            {
                failureReason = InteractionFailureReason.None;
                return true;
            }

            return PermissionService.CanInteract(request, out failureReason);
        }

        private void Awake()
        {
            if (Active != null && Active != this)
            {
                Debug.LogWarning("[FishLanternXR] Replacing an existing InteractionServiceHub.", this);
            }

            Active = this;
            ValidateProviders();
        }

        private void OnDestroy()
        {
            if (Active == this)
                Active = null;
        }

        private void OnValidate()
        {
            ValidateProviders();
        }

        private void ValidateProviders()
        {
            ValidateProvider<IInteractionEventReporter>(eventReporter, nameof(eventReporter));
            ValidateProvider<IInteractionPermissionService>(permissionService, nameof(permissionService));
            ValidateProvider<IInteractionFeedbackService>(feedbackService, nameof(feedbackService));
            ValidateProvider<ITutorialPromptService>(tutorialPromptService, nameof(tutorialPromptService));
        }

        private void ValidateProvider<TContract>(MonoBehaviour provider, string fieldName)
        {
            if (provider != null && provider is not TContract)
            {
                Debug.LogError(
                    $"[FishLanternXR] Provider '{provider.name}' assigned to {fieldName} " +
                    $"does not implement {typeof(TContract).Name}.",
                    this);
            }
        }
    }
}
