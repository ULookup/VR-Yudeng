using System;
using FishLantern.XR;
using UnityEngine;
using UnityEngine.UI;

namespace Yudeng.Presentation
{
    [Serializable]
    public sealed class PaperingWorkpiece
    {
        [SerializeField] private string displayName;
        [SerializeField] private PaperPieceVisualState state;
        [SerializeField] private GameObject cuttingRoot;
        [SerializeField] private GameObject loosePieceRoot;
        [SerializeField] private GameObject glueRoot;
        [SerializeField] private GameObject socketGuideRoot;

        public string DisplayName => displayName;
        public PaperPieceVisualState State => state;

        public PaperingWorkpiece(string name, PaperPieceVisualState pieceState,
            GameObject cut, GameObject loose, GameObject glue, GameObject socketGuide)
        {
            displayName = name ?? string.Empty;
            state = pieceState;
            cuttingRoot = cut;
            loosePieceRoot = loose;
            glueRoot = glue;
            socketGuideRoot = socketGuide;
        }

        public void ShowWorkstation(bool current)
        {
            var available = current && state != null && !state.IsAttached;
            SetVisible(cuttingRoot, available && !state.IsCut);
            SetVisible(loosePieceRoot, available && state.IsCut);
            SetVisible(glueRoot, available && state.IsCut);
            SetVisible(socketGuideRoot, available && state.IsGlueApplied);
        }

        private static void SetVisible(GameObject item, bool visible)
        {
            if (item != null && item.activeSelf != visible) item.SetActive(visible);
        }
    }

    /// <summary>Exposes only the next physical patch; the existing stage controller owns completion.</summary>
    [DisallowMultipleComponent]
    public sealed class PaperingGuide : MonoBehaviour, IInteractionResettable
    {
        [SerializeField] private MonoBehaviour stepSource;
        [SerializeField] private PaperingWorkpiece[] pieces = Array.Empty<PaperingWorkpiece>();
        [SerializeField] private Text instructionText;
        [SerializeField] private Image pieceProgressFill;
        private IInteractionStepProvider stepProvider;
        private bool refreshRequested;

        public event Action InstructionChanged;
        public string CurrentInstruction { get; private set; } = string.Empty;
        public int CurrentPieceIndex { get; private set; } = -1;
        public int PieceCount => pieces.Length;

        public void Configure(MonoBehaviour steps, PaperingWorkpiece[] workpieces,
            Text instruction = null, Image progress = null)
        {
            Unsubscribe();
            stepSource = steps;
            pieces = workpieces ?? Array.Empty<PaperingWorkpiece>();
            instructionText = instruction;
            pieceProgressFill = progress;
            if (isActiveAndEnabled) Subscribe();
            Refresh();
        }

        public void ResetInteraction()
        {
            // The controller resets the real interactions and each visual state separately.
            // Delay workstation activation until that entire reset pass has completed.
            refreshRequested = true;
        }

        private void OnEnable()
        {
            Subscribe();
            refreshRequested = true;
        }

        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            Unsubscribe();
            stepProvider = stepSource as IInteractionStepProvider;
            stepProvider?.StepChanged.AddListener(HandleStepChanged);
            foreach (var piece in pieces)
                if (piece?.State != null) piece.State.StateChanged += HandlePieceChanged;
        }

        private void Unsubscribe()
        {
            stepProvider?.StepChanged.RemoveListener(HandleStepChanged);
            stepProvider = null;
            foreach (var piece in pieces)
                if (piece?.State != null) piece.State.StateChanged -= HandlePieceChanged;
        }

        private void HandleStepChanged(FishLanternStep _) => refreshRequested = true;
        private void HandlePieceChanged() => refreshRequested = true;

        private void LateUpdate()
        {
            // Never disable a cut trigger or socketed object from inside its interaction callback.
            if (refreshRequested) Refresh();
        }

        private void Refresh()
        {
            refreshRequested = false;
            var papering = (stepProvider ?? stepSource as IInteractionStepProvider)?.CurrentStep == FishLanternStep.Papering;
            CurrentPieceIndex = -1;
            for (var index = 0; index < pieces.Length; index++)
            {
                var piece = pieces[index];
                if (papering && CurrentPieceIndex < 0 && piece?.State != null && !piece.State.IsAttached)
                    CurrentPieceIndex = index;
                piece?.ShowWorkstation(papering && index == CurrentPieceIndex);
            }

            var current = CurrentPieceIndex >= 0 ? pieces[CurrentPieceIndex] : null;
            CurrentInstruction = BuildInstruction(current, papering);
            if (instructionText != null) instructionText.text = CurrentInstruction;
            if (pieceProgressFill != null)
                pieceProgressFill.fillAmount = current?.State != null ? current.State.Progress : papering ? 1f : 0f;
            InstructionChanged?.Invoke();
        }

        private string BuildInstruction(PaperingWorkpiece current, bool papering)
        {
            if (!papering) return string.Empty;
            if (current == null) return "整鱼纸面已贴合完成，裁剪、涂胶与贴合成果已保留。";
            var title = $"第 {CurrentPieceIndex + 1} / {pieces.Length} 片 · {current.DisplayName}";
            if (!current.State.IsCut)
                return title + "\n握住剪刀并按住扳机，让刀口按顺序经过纸样边缘标记，完成后可拿起纸片。";
            if (!current.State.IsGlueApplied)
                return title + "\n握住胶水并按住扳机，用瓶嘴涂遍骨架对应的三个区域；已涂部分会保留。";
            return title + "\n抓取裁好的纸片，对准同名贴合位置后松手；正确贴合后开始下一片。";
        }
    }
}
