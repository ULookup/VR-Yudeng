using System;
using FishLantern.XR;
using UnityEngine;

namespace Yudeng.Presentation
{
    [Serializable]
    public sealed class FrameAssemblyVisualBinding
    {
        [SerializeField] private string sourceId;
        [SerializeField] private InteractionKind completionKind;
        [SerializeField] private InteractionResult requiredResult = InteractionResult.Succeeded;
        [SerializeField] private string targetId;
        [SerializeField] private Renderer[] looseRenderers;
        [SerializeField] private GameObject[] installedVisuals;

        public FrameAssemblyVisualBinding(string source, InteractionKind kind, string target,
            Renderer[] loose, GameObject[] installed, InteractionResult result = InteractionResult.Succeeded)
        {
            sourceId = source;
            completionKind = kind;
            requiredResult = result;
            targetId = target;
            looseRenderers = loose;
            installedVisuals = installed;
        }

        public bool Matches(InteractionEventData data) => data.Result == requiredResult && data.ObjectId == sourceId &&
            data.InteractionKind == completionKind &&
            (string.IsNullOrEmpty(targetId) || data.TargetId == targetId);

        public void Apply(bool installed)
        {
            if (looseRenderers != null)
                foreach (var renderer in looseRenderers)
                    if (renderer != null) renderer.enabled = !installed;
            if (installedVisuals != null)
                foreach (var visual in installedVisuals)
                    if (visual != null) visual.SetActive(installed);
        }
    }

    /// <summary>Shows fitted strips and bindings only after the existing physical interaction succeeds.</summary>
    [DisallowMultipleComponent]
    public sealed class FrameAssemblyVisualState : MonoBehaviour, IInteractionResettable
    {
        [SerializeField] private MonoBehaviour eventSource;
        [SerializeField] private FrameAssemblyVisualBinding[] bindings = Array.Empty<FrameAssemblyVisualBinding>();
        private IInteractionEventStream stream;

        public void Configure(MonoBehaviour events, FrameAssemblyVisualBinding[] visualBindings)
        {
            Unsubscribe();
            eventSource = events;
            bindings = visualBindings ?? Array.Empty<FrameAssemblyVisualBinding>();
            ResetInteraction();
            if (isActiveAndEnabled) Subscribe();
        }

        public void ResetInteraction()
        {
            foreach (var binding in bindings) binding?.Apply(false);
        }

        private void Awake() => ResetInteraction();
        private void OnEnable() => Subscribe();
        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            Unsubscribe();
            stream = eventSource as IInteractionEventStream;
            stream?.InteractionReported.AddListener(HandleInteraction);
        }

        private void Unsubscribe()
        {
            stream?.InteractionReported.RemoveListener(HandleInteraction);
            stream = null;
        }

        private void HandleInteraction(InteractionEventData data)
        {
            if (data == null || (data.Result != InteractionResult.Succeeded && data.Result != InteractionResult.Progressed) ||
                data.Step != FishLanternStep.FrameMaking)
                return;
            foreach (var binding in bindings)
                if (binding != null && binding.Matches(data)) binding.Apply(true);
        }
    }
}
