using UnityEngine;

namespace FishLantern.XR
{
    /// <summary>Shows one content root only while its authoritative step is active.</summary>
    [DisallowMultipleComponent]
    public sealed class StepContentController : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour stepSource;
        [SerializeField] private FishLanternStep visibleDuringStep;
        [SerializeField] private GameObject contentRoot;

        private IInteractionStepProvider provider;

        /// <summary>Step during which this controller shows its content.</summary>
        public FishLanternStep VisibleDuringStep => visibleDuringStep;

        /// <summary>Content root toggled by the authoritative step.</summary>
        public GameObject ContentRoot => contentRoot;

        /// <summary>Configures the authoritative source and content root.</summary>
        public void Configure(MonoBehaviour source, FishLanternStep step, GameObject content)
        {
            Unsubscribe();
            stepSource = source;
            visibleDuringStep = step;
            contentRoot = content;
            Subscribe();
            Apply(provider != null ? provider.CurrentStep : FishLanternStep.None);
        }

        private void OnEnable()
        {
            Subscribe();
            Apply(provider != null ? provider.CurrentStep : FishLanternStep.None);
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            Unsubscribe();
            provider = stepSource as IInteractionStepProvider;
            provider?.StepChanged.AddListener(Apply);
        }

        private void Unsubscribe()
        {
            provider?.StepChanged.RemoveListener(Apply);
            provider = null;
        }

        private void Apply(FishLanternStep step)
        {
            if (contentRoot != null)
                contentRoot.SetActive(step == visibleDuringStep);
        }
    }
}
