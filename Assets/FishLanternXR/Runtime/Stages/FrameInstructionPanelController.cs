using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FishLantern.XR
{
    /// <summary>One ordered, stable-ID-driven instruction in the frame-making tutorial.</summary>
    [Serializable]
    public sealed class FrameInstructionStep
    {
        [SerializeField] private string itemName;
        [SerializeField] private string eventObjectId;
        [SerializeField] private InteractionKind completionKind;
        [SerializeField] private string targetId;
        [SerializeField, TextArea] private string rotationHint;
        [SerializeField, TextArea] private string placementHint;
        [SerializeField] private InteractableIdentity highlightTarget;
        [SerializeField] private GameObject interactionTargetRoot;
        [SerializeField] private bool keepTargetAfterCompletion;

        public string ItemName => itemName;
        public string EventObjectId => eventObjectId;
        public InteractionKind CompletionKind => completionKind;
        public string TargetId => targetId;
        public string RotationHint => rotationHint;
        public string PlacementHint => placementHint;
        public InteractableIdentity HighlightTarget => highlightTarget;
        public GameObject InteractionTargetRoot => interactionTargetRoot;
        public bool KeepTargetAfterCompletion => keepTargetAfterCompletion;

        public FrameInstructionStep(
            string displayItemName,
            string sourceObjectId,
            InteractionKind completedBy,
            string requiredTargetId,
            string rotateInstruction,
            string placeInstruction,
            InteractableIdentity objectToHighlight,
            GameObject targetRoot = null,
            bool keepTarget = false)
        {
            itemName = displayItemName ?? string.Empty;
            eventObjectId = sourceObjectId ?? string.Empty;
            completionKind = completedBy;
            targetId = requiredTargetId ?? string.Empty;
            rotationHint = rotateInstruction ?? string.Empty;
            placementHint = placeInstruction ?? string.Empty;
            highlightTarget = objectToHighlight;
            interactionTargetRoot = targetRoot;
            keepTargetAfterCompletion = keepTarget;
        }

        public bool Matches(InteractionEventData eventData)
        {
            return eventData != null &&
                   eventData.Result == InteractionResult.Succeeded &&
                   eventData.ObjectId == eventObjectId &&
                   eventData.InteractionKind == completionKind &&
                   (string.IsNullOrWhiteSpace(targetId) || eventData.TargetId == targetId);
        }
    }

    /// <summary>
    /// Presents and enforces the ordered body-then-fin substeps without taking ownership of the main game state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FrameInstructionPanelController : MonoBehaviour, IInteractionResettable
    {
        [SerializeField] private MonoBehaviour eventSource;
        [SerializeField] private MonoBehaviour stepSource;
        [SerializeField] private List<FrameInstructionStep> instructions = new();
        [SerializeField] private Text sectionText;
        [SerializeField] private Text progressText;
        [SerializeField] private Text itemText;
        [SerializeField] private Text rotationText;
        [SerializeField] private Text placementText;
        [SerializeField] private Text statusText;

        private IInteractionEventStream stream;
        private IInteractionStepProvider stepProvider;
        private int currentIndex;
        private InteractableIdentity highlightedIdentity;

        public int CurrentIndex => currentIndex;
        public int StepCount => instructions.Count;
        public string CurrentEventObjectId =>
            currentIndex >= 0 && currentIndex < instructions.Count
                ? instructions[currentIndex].EventObjectId
                : string.Empty;

        public void Configure(
            MonoBehaviour events,
            MonoBehaviour steps,
            IEnumerable<FrameInstructionStep> orderedInstructions)
        {
            Unsubscribe();
            eventSource = events;
            stepSource = steps;
            instructions = orderedInstructions == null
                ? new List<FrameInstructionStep>()
                : new List<FrameInstructionStep>(orderedInstructions);
            ResetInteraction();
            Subscribe();
        }

        public void BindUi(
            Text section,
            Text progress,
            Text item,
            Text rotation,
            Text placement,
            Text status)
        {
            sectionText = section;
            progressText = progress;
            itemText = item;
            rotationText = rotation;
            placementText = placement;
            statusText = status;
            Render();
        }

        public void ResetInteraction()
        {
            SetHighlight(null);
            currentIndex = 0;
            RefreshTargets();
            Render();
        }

        /// <summary>Re-applies the current authoritative step for editor previews and integration refreshes.</summary>
        public void RefreshForCurrentStep()
        {
            var currentProvider = stepProvider ?? stepSource as IInteractionStepProvider;
            ApplyStep(currentProvider != null ? currentProvider.CurrentStep : FishLanternStep.None);
        }

        private void Awake()
        {
            ResetInteraction();
        }

        private void OnEnable()
        {
            Subscribe();
            ApplyStep(stepProvider != null ? stepProvider.CurrentStep : FishLanternStep.None);
        }

        private void OnDisable()
        {
            SetHighlight(null);
            Unsubscribe();
        }

        private void OnDestroy()
        {
            SetHighlight(null);
        }

        private void Subscribe()
        {
            stream = eventSource as IInteractionEventStream;
            stepProvider = stepSource as IInteractionStepProvider;
            stream?.InteractionReported.RemoveListener(HandleEvent);
            stream?.InteractionReported.AddListener(HandleEvent);
            stepProvider?.StepChanged.RemoveListener(ApplyStep);
            stepProvider?.StepChanged.AddListener(ApplyStep);
        }

        private void Unsubscribe()
        {
            stream?.InteractionReported.RemoveListener(HandleEvent);
            stepProvider?.StepChanged.RemoveListener(ApplyStep);
            stream = null;
            stepProvider = null;
        }

        private void ApplyStep(FishLanternStep step)
        {
            if (step == FishLanternStep.FrameMaking)
                ResetInteraction();
            else
            {
                SetHighlight(null);
                RefreshTargets(activeStage: false);
                Render();
            }
        }

        private void HandleEvent(InteractionEventData eventData)
        {
            if (eventData == null || eventData.Step != FishLanternStep.FrameMaking ||
                currentIndex < 0 || currentIndex >= instructions.Count)
                return;

            var current = instructions[currentIndex];
            if (eventData.Result == InteractionResult.Failed &&
                (eventData.ObjectId == current.EventObjectId ||
                 eventData.ObjectId == current.HighlightTarget?.StableId))
            {
                if (statusText != null)
                    statusText.text = $"⚠ 未完成：{FailureLabel(eventData.FailureReason)}";
                return;
            }

            if (!current.Matches(eventData))
                return;

            currentIndex++;
            RefreshTargets();
            Render(completedPrevious: true);
        }

        private void RefreshTargets(bool activeStage = true)
        {
            for (var index = 0; index < instructions.Count; index++)
            {
                var instruction = instructions[index];
                if (instruction.InteractionTargetRoot == null)
                    continue;

                var shouldShow = activeStage &&
                                 (index == currentIndex ||
                                  (index < currentIndex && instruction.KeepTargetAfterCompletion));
                instruction.InteractionTargetRoot.SetActive(shouldShow);
            }

            var nextHighlight = activeStage && currentIndex >= 0 && currentIndex < instructions.Count
                ? instructions[currentIndex].HighlightTarget
                : null;
            SetHighlight(nextHighlight);
        }

        private void SetHighlight(InteractableIdentity identity)
        {
            if (highlightedIdentity == identity)
                return;

            if (highlightedIdentity != null)
                InteractionServiceHub.Active?.FeedbackService?.SetHighlight(highlightedIdentity, false);

            highlightedIdentity = identity;
            if (highlightedIdentity != null)
                InteractionServiceHub.Active?.FeedbackService?.SetHighlight(highlightedIdentity, true);
        }

        private void Render(bool completedPrevious = false)
        {
            if (instructions.Count == 0)
                return;

            var currentProvider = stepProvider ?? stepSource as IInteractionStepProvider;
            var isFrameStage = currentProvider != null &&
                               currentProvider.CurrentStep == FishLanternStep.FrameMaking;
            if (!isFrameStage)
            {
                SetText(sectionText, "第一阶段操作步骤");
                SetText(progressText, "点击“开始”后显示扎骨架步骤");
                SetText(itemText, string.Empty);
                SetText(rotationText, string.Empty);
                SetText(placementText, string.Empty);
                SetText(statusText, string.Empty);
                return;
            }

            if (currentIndex >= instructions.Count)
            {
                SetText(sectionText, "扎骨架 · 已完成");
                SetText(progressText, $"{instructions.Count} / {instructions.Count}");
                SetText(itemText, "前段鱼身与六件鱼鳍已完成");
                SetText(rotationText, string.Empty);
                SetText(placementText, "预制鱼头、后段和鱼尾已组合，点击“下一步”开始糊纸");
                SetText(statusText, "✓ 本阶段完成");
                return;
            }

            var current = instructions[currentIndex];
            var bodyStepCount = Mathf.Min(10, instructions.Count);
            SetText(sectionText, currentIndex < bodyStepCount ? "扎骨架 · 身体" : "扎骨架 · 鱼鳍");
            SetText(progressText, $"步骤 {currentIndex + 1} / {instructions.Count}");
            SetText(itemText, "拿取：" + current.ItemName);
            SetText(rotationText, "旋转：" + current.RotationHint);
            SetText(placementText, "位置：" + current.PlacementHint);
            SetText(statusText, completedPrevious ? "✓ 上一步完成，请继续" : "请按提示操作");
        }

        private static void SetText(Text target, string value)
        {
            if (target != null)
                target.text = value;
        }

        private static string FailureLabel(InteractionFailureReason reason)
        {
            return reason switch
            {
                InteractionFailureReason.WrongObject => "拿取物品不正确",
                InteractionFailureReason.WrongHand => "请使用提示的手",
                InteractionFailureReason.PrerequisiteMissing => "请先完成上一步",
                InteractionFailureReason.NotBent => "竹条尚未弯曲完成",
                InteractionFailureReason.Misaligned => "方向或位置未对齐",
                InteractionFailureReason.InvalidTarget => "放置目标不正确",
                _ => "请检查物品、方向与目标位置",
            };
        }
    }
}
