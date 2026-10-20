using System;
using FishLantern.XR;
using UnityEngine;
using UnityEngine.UI;

namespace Yudeng.Presentation
{
    /// <summary>Shows the next paper surface and reads actual drawing coverage without completing work.</summary>
    [DisallowMultipleComponent]
    public sealed class SketchAndPaintGuide : MonoBehaviour, IInteractionResettable
    {
        [SerializeField] private MonoBehaviour stepSource;
        [SerializeField] private PaperPaintSurface[] surfaces = Array.Empty<PaperPaintSurface>();
        [SerializeField] private PaperPaintTool brush;
        [SerializeField] private Text instructionText;
        [SerializeField] private Image pieceProgressFill;

        private IInteractionStepProvider stepProvider;
        private bool refreshRequested;

        public event Action InstructionChanged;
        public string CurrentInstruction { get; private set; } = string.Empty;
        public int CurrentSurfaceIndex { get; private set; } = -1;
        public int SurfaceCount => surfaces.Length;
        public PaperPaintSurface CurrentSurface => CurrentSurfaceIndex >= 0 && CurrentSurfaceIndex < surfaces.Length
            ? surfaces[CurrentSurfaceIndex] : null;

        public float OverallProgress
        {
            get
            {
                var total = 0f;
                var count = 0;
                foreach (var surface in surfaces)
                {
                    if (surface == null) continue;
                    total += (surface.SketchProgress + surface.PaintProgress) * 0.5f;
                    count++;
                }
                return count > 0 ? Mathf.Clamp01(total / count) : 0f;
            }
        }

        public bool CanResetCurrentOperation => CurrentSurface != null && !CurrentSurface.IsPaintComplete;

        public void Configure(MonoBehaviour steps, PaperPaintSurface[] paperSurfaces, PaperPaintTool paintingBrush,
            Text instruction = null, Image pieceProgress = null)
        {
            Unsubscribe();
            stepSource = steps;
            surfaces = paperSurfaces ?? Array.Empty<PaperPaintSurface>();
            brush = paintingBrush;
            instructionText = instruction;
            pieceProgressFill = pieceProgress;
            if (isActiveAndEnabled) Subscribe();
            Refresh();
        }

        /// <summary>Restores only an unfinished operation; completed event requirements remain valid.</summary>
        public bool ResetCurrentOperation()
        {
            var surface = CurrentSurface;
            if (CurrentStep != FishLanternStep.SketchAndPaint || surface == null || surface.IsPaintComplete)
                return false;
            var restored = surface.ResetUnfinishedOperation();
            refreshRequested = true;
            return restored;
        }

        public void ClearStrokeContinuity()
        {
            foreach (var surface in surfaces)
                if (surface != null) surface.ClearStrokeContinuity();
        }

        public void ResetInteraction()
        {
            // Every real surface is reset by the existing experience reset pass, including inactive ones.
            // Refresh after that pass rather than recreating or clearing painting state in this presenter.
            CurrentSurfaceIndex = -1;
            ClearStrokeContinuity();
            refreshRequested = true;
        }

        private FishLanternStep CurrentStep =>
            (stepProvider ?? stepSource as IInteractionStepProvider)?.CurrentStep ?? FishLanternStep.None;

        private void OnEnable()
        {
            Subscribe();
            refreshRequested = true;
        }

        private void OnDisable()
        {
            Unsubscribe();
            foreach (var surface in surfaces)
                if (surface != null) surface.SetWorkEnabled(false);
            ClearStrokeContinuity();
        }

        private void Subscribe()
        {
            Unsubscribe();
            stepProvider = stepSource as IInteractionStepProvider;
            stepProvider?.StepChanged.AddListener(HandleStepChanged);
            foreach (var surface in surfaces)
                if (surface != null) surface.StateChanged += RequestRefresh;
            if (brush != null) brush.ColorChanged += RequestRefresh;
        }

        private void Unsubscribe()
        {
            stepProvider?.StepChanged.RemoveListener(HandleStepChanged);
            stepProvider = null;
            foreach (var surface in surfaces)
                if (surface != null) surface.StateChanged -= RequestRefresh;
            if (brush != null) brush.ColorChanged -= RequestRefresh;
        }

        private void HandleStepChanged(FishLanternStep _)
        {
            ClearStrokeContinuity();
            refreshRequested = true;
        }

        private void RequestRefresh() => refreshRequested = true;

        private void LateUpdate()
        {
            // Keep work availability changes out of the completion event broadcast.
            if (refreshRequested) Refresh();
        }

        private void Refresh()
        {
            refreshRequested = false;
            var painting = CurrentStep == FishLanternStep.SketchAndPaint;
            CurrentSurfaceIndex = -1;
            for (var index = 0; index < surfaces.Length; index++)
            {
                var surface = surfaces[index];
                if (surface == null) continue;
                if (painting && CurrentSurfaceIndex < 0 && !surface.IsPaintComplete)
                    CurrentSurfaceIndex = index;
                surface.SetWorkEnabled(painting && index == CurrentSurfaceIndex);
            }

            var current = CurrentSurface;
            var instruction = BuildInstruction(current, painting);
            if (instructionText != null && instructionText.text != instruction) instructionText.text = instruction;
            if (pieceProgressFill != null)
                pieceProgressFill.fillAmount = current != null
                    ? (current.SketchProgress + current.PaintProgress) * 0.5f
                    : painting && surfaces.Length > 0 ? 1f : 0f;
            CurrentInstruction = instruction;
            // Coverage changes also refresh the experience's graphical progress, even when wording is unchanged.
            InstructionChanged?.Invoke();
        }

        private string BuildInstruction(PaperPaintSurface surface, bool painting)
        {
            if (!painting) return string.Empty;
            if (surfaces.Length == 0) return "绘画纸面尚未接入，请检查工坊配置。";
            if (surface == null) return "各片纸面的描线与上色已完成，作品保留并进入装灯工序。";
            var title = $"第 {CurrentSurfaceIndex + 1} / {surfaces.Length} 片 · {surface.SurfaceLabel}";
            if (!surface.IsSketchComplete)
                return title + "\n握住铅笔并按住扳机，用笔尖沿纸面提示线移动；只接触同一点不会完成描线。" +
                    "\n点击“转动鱼灯”可转到另一侧；“重绘当前工序”只清除这片尚未完成的描线。";

            var requiredColor = surface.RequiredColorName;
            var colorInstruction = requiredColor is "朱红" or "石绿" or "墨黑"
                ? $"用笔尖接触颜料碟蘸取{requiredColor}，再涂过对应纸面区域。"
                : "按纸面提示蘸取对应颜料，补全尚未涂满的纹样。";
            var brushColor = brush != null && brush.HasPaint ? $"画笔当前颜色：{brush.ColorLabel}。" : "画笔尚未蘸取颜料。";
            return title + "\n握住画笔并按住扳机，" + colorInstruction +
                $"\n{brushColor}“重绘当前工序”只清除这片尚未完成的上色，已完成描线会保留。";
        }
    }
}
