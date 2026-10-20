using System;
using FishLantern.XR;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Yudeng.Presentation
{
    /// <summary>Consumes real cutting, glue and socket events to present one lasting paper patch.</summary>
    [DisallowMultipleComponent]
    public sealed class PaperPieceVisualState : MonoBehaviour, IInteractionResettable
    {
        [SerializeField] private MonoBehaviour eventSource;
        [SerializeField] private string cutSourceId;
        [SerializeField] private string glueSourceId;
        [SerializeField] private GrabInteractableEx paperPiece;
        [SerializeField] private string requiredSocketId;
        [SerializeField] private GameObject[] uncutVisuals = Array.Empty<GameObject>();
        [SerializeField] private Renderer[] looseRenderers = Array.Empty<Renderer>();
        [SerializeField] private GameObject[] appliedVisuals = Array.Empty<GameObject>();
        [SerializeField] private GameObject[] cutCheckpointVisuals = Array.Empty<GameObject>();
        [SerializeField] private GameObject[] cutSegmentVisuals = Array.Empty<GameObject>();

        private IInteractionEventStream stream;
        private InteractableIdentity pieceIdentity;
        private int visitedCheckpoints;
        private bool refreshRequested;

        public event Action StateChanged;
        public bool IsCut { get; private set; }
        public bool IsGlueApplied { get; private set; }
        public bool IsAttached { get; private set; }
        public float CutProgress { get; private set; }
        public float GlueProgress { get; private set; }
        public float Progress => (CutProgress + GlueProgress + (IsAttached ? 1f : 0f)) / 3f;

        public void Configure(MonoBehaviour events, string cutId, string glueId,
            GrabInteractableEx piece, string socketId, GameObject[] uncut,
            Renderer[] loose, GameObject[] applied, GameObject[] cutCheckpoints = null,
            GameObject[] cutSegments = null)
        {
            Unsubscribe();
            eventSource = events;
            cutSourceId = cutId ?? string.Empty;
            glueSourceId = glueId ?? string.Empty;
            paperPiece = piece;
            requiredSocketId = socketId ?? string.Empty;
            uncutVisuals = uncut ?? Array.Empty<GameObject>();
            looseRenderers = loose ?? Array.Empty<Renderer>();
            appliedVisuals = applied ?? Array.Empty<GameObject>();
            cutCheckpointVisuals = cutCheckpoints ?? Array.Empty<GameObject>();
            cutSegmentVisuals = cutSegments ?? Array.Empty<GameObject>();
            pieceIdentity = paperPiece != null ? paperPiece.GetComponent<InteractableIdentity>() : null;
            ResetInteraction();
            if (isActiveAndEnabled) Subscribe();
        }

        public void ResetInteraction()
        {
            IsCut = false;
            IsGlueApplied = false;
            IsAttached = false;
            CutProgress = 0f;
            GlueProgress = 0f;
            visitedCheckpoints = 0;
            refreshRequested = false;
            ApplyVisuals();
            StateChanged?.Invoke();
        }

        private void Awake()
        {
            pieceIdentity = paperPiece != null ? paperPiece.GetComponent<InteractableIdentity>() : null;
            ResetInteraction();
        }

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
            if (data == null || data.Step != FishLanternStep.Papering || IsAttached ||
                (data.Result != InteractionResult.Progressed && data.Result != InteractionResult.Succeeded)) return;

            var changed = false;
            if (!string.IsNullOrEmpty(cutSourceId) && data.ObjectId == cutSourceId)
            {
                if (data.InteractionKind == InteractionKind.CutProgress && data.Result == InteractionResult.Progressed)
                {
                    CutProgress = data.Progress;
                    if (int.TryParse(data.TargetId, out var checkpoint))
                        visitedCheckpoints = Mathf.Max(visitedCheckpoints, checkpoint + 1);
                    changed = true;
                }
                else if (data.InteractionKind == InteractionKind.CutComplete && data.Result == InteractionResult.Succeeded)
                {
                    IsCut = true;
                    CutProgress = 1f;
                    visitedCheckpoints = cutCheckpointVisuals.Length;
                    changed = true;
                }
            }
            if (!string.IsNullOrEmpty(glueSourceId) && data.ObjectId == glueSourceId && IsCut)
            {
                if (data.InteractionKind == InteractionKind.GlueProgress && data.Result == InteractionResult.Progressed)
                {
                    GlueProgress = data.Progress;
                    changed = true;
                }
                else if (data.InteractionKind == InteractionKind.GlueApplied && data.Result == InteractionResult.Succeeded)
                {
                    IsGlueApplied = true;
                    GlueProgress = 1f;
                    changed = true;
                }
            }
            if (pieceIdentity != null && data.ObjectId == pieceIdentity.StableId &&
                data.TargetId == requiredSocketId && !string.IsNullOrEmpty(requiredSocketId) &&
                data.InteractionKind == InteractionKind.Socketed && data.Result == InteractionResult.Succeeded &&
                IsCut && IsGlueApplied)
            {
                IsAttached = true;
                paperPiece.MarkFixedInPlace();
                changed = true;
            }
            if (!changed) return;
            // Keep every prerequisite subscriber alive until the complete event broadcast has finished.
            refreshRequested = true;
            StateChanged?.Invoke();
        }

        private void LateUpdate()
        {
            if (!refreshRequested) return;
            refreshRequested = false;
            if (IsAttached && paperPiece != null && paperPiece.isSelected && paperPiece.interactionManager != null)
                paperPiece.interactionManager.CancelInteractableSelection((IXRSelectInteractable)paperPiece);
            ApplyVisuals();
        }

        private void ApplyVisuals()
        {
            // This component stays enabled on the logic root. Workstation roots are owned by PaperingGuide.
            if (paperPiece != null && !IsAttached) paperPiece.enabled = IsCut;
            SetVisible(uncutVisuals, !IsCut);
            foreach (var loose in looseRenderers)
                if (loose != null) loose.enabled = IsCut && !IsAttached;
            SetVisible(appliedVisuals, IsAttached);
            for (var index = 0; index < cutCheckpointVisuals.Length; index++)
                if (cutCheckpointVisuals[index] != null)
                    cutCheckpointVisuals[index].SetActive(!IsCut && index >= visitedCheckpoints);
            for (var index = 0; index < cutSegmentVisuals.Length; index++)
                if (cutSegmentVisuals[index] != null)
                    cutSegmentVisuals[index].SetActive(!IsCut && index < visitedCheckpoints);
        }

        private static void SetVisible(GameObject[] objects, bool visible)
        {
            foreach (var item in objects)
                if (item != null) item.SetActive(visible);
        }
    }
}
