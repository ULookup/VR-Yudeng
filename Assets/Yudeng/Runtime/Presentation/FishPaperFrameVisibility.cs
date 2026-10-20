using System;
using FishLantern.XR;
using UnityEngine;

namespace Yudeng.Presentation
{
    /// <summary>Keeps the actual assembled skeleton available throughout later production stages.</summary>
    [DisallowMultipleComponent]
    public sealed class FishPaperFrameVisibility : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour stepSource;
        [SerializeField] private GameObject[] frameRoots = Array.Empty<GameObject>();
        [SerializeField] private Transform presentedFrame;
        [SerializeField] private Transform paperingAnchor;
        private IInteractionStepProvider stepProvider;
        private bool presentationPoseApplied;

        public void Configure(MonoBehaviour steps, GameObject[] roots)
        {
            Unsubscribe();
            stepSource = steps;
            frameRoots = roots ?? Array.Empty<GameObject>();
            if (isActiveAndEnabled) Subscribe();
            Apply((stepSource as IInteractionStepProvider)?.CurrentStep ?? FishLanternStep.None);
        }

        public void ConfigurePresentationPose(Transform frame, Transform anchor)
        {
            presentedFrame = frame;
            paperingAnchor = anchor;
        }

        private void OnEnable()
        {
            Subscribe();
            Apply(stepProvider?.CurrentStep ?? FishLanternStep.None);
        }

        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            Unsubscribe();
            stepProvider = stepSource as IInteractionStepProvider;
            stepProvider?.StepChanged.AddListener(Apply);
        }

        private void Unsubscribe()
        {
            stepProvider?.StepChanged.RemoveListener(Apply);
            stepProvider = null;
        }

        private void Apply(FishLanternStep step)
        {
            if (step is FishLanternStep.None or FishLanternStep.Welcome)
                presentationPoseApplied = false;
            if (step == FishLanternStep.Papering && !presentationPoseApplied &&
                presentedFrame != null && paperingAnchor != null)
            {
                // Move the completed physical artifact once; GrabInteractableEx retains its original reset pose.
                presentedFrame.SetPositionAndRotation(paperingAnchor.position, paperingAnchor.rotation);
                var body = presentedFrame.GetComponent<Rigidbody>();
                if (body != null)
                {
                    body.position = paperingAnchor.position;
                    body.rotation = paperingAnchor.rotation;
                }
                presentationPoseApplied = true;
                Physics.SyncTransforms();
            }
            var visible = step is FishLanternStep.FrameMaking or FishLanternStep.Papering or
                FishLanternStep.SketchAndPaint or FishLanternStep.LampDisplay or FishLanternStep.Complete;
            foreach (var root in frameRoots)
                if (root != null && root.activeSelf != visible) root.SetActive(visible);
        }
    }
}
