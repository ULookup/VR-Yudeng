using System;
using System.Collections.Generic;
using FishLantern.XR;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using Yudeng.Experience;
using Yudeng.Input;

namespace Yudeng.Presentation
{
    /// <summary>Reads the existing workshop and XRI state; never advances or resets a crafting operation.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(200)]
    public sealed class WorkshopSpatialInterface : MonoBehaviour
    {
        [SerializeField] private FishLanternExperienceController experience;
        [SerializeField] private DesktopXrInputRig desktopInput;
        [SerializeField] private Text instructionSource;
        [SerializeField] private Text[] frameSources = Array.Empty<Text>();
        [SerializeField] private Text heading;
        [SerializeField] private Text instruction;
        [SerializeField] private Text heldTool;
        [SerializeField] private Text progressLabel;
        [SerializeField] private Text feedback;
        [SerializeField] private Text inputHint;
        [SerializeField] private Image[] stageFills = Array.Empty<Image>();
        [SerializeField] private Text[] stageLabels = Array.Empty<Text>();
        [SerializeField] private Image[] stageIcons = Array.Empty<Image>();
        [SerializeField] private Button startButton;
        [SerializeField] private Button nextButton;
        [SerializeField] private Button restartButton;
        [SerializeField] private XRBaseInteractor[] handInteractors = Array.Empty<XRBaseInteractor>();
        [SerializeField] private XRRayInteractor[] rays = Array.Empty<XRRayInteractor>();
        [SerializeField] private Text[] labels = Array.Empty<Text>();
        [SerializeField] private Text controlsBody;
        [SerializeField] private Text controlsButtonLabel;
        [SerializeField] private Button previousReadingPage;
        [SerializeField] private Button nextReadingPage;
        [SerializeField] private Text readingPageLabel;
        [SerializeField] private Image feedbackBackground;
        [SerializeField] private Button[] readableButtons = Array.Empty<Button>();
        [SerializeField] private Text[] readableButtonLabels = Array.Empty<Text>();
        [SerializeField] private Button[] artworkButtons = Array.Empty<Button>();
        [SerializeField] private Image[] buttonIcons = Array.Empty<Image>();
        [SerializeField] private int pauseArtworkIndex = -1;
        [SerializeField] private Sprite pauseArtwork;
        [SerializeField] private Sprite resumeArtwork;

        private Font runtimeFont;
        private float nextRefresh;
        private readonly List<string> readingPages = new();
        private readonly TextGenerator readingLayout = new();
        private string lastReadingContent;
        private Text lastReadingTarget;
        private Vector2 lastReadingSize;
        private int readingPage;
        private bool controlsExpanded;
        public bool ControlsExpanded => controlsExpanded;
        public int CurrentReadingPage => readingPage + 1;
        public int ReadingPageCount => readingPages.Count;
        private static readonly string[] StageNames = { "扎骨架", "裁剪糊纸", "描线上色", "装灯展示" };
        private readonly Color activeColor = new(0.97f, 0.76f, 0.36f);
        private readonly Color completedColor = new(0.42f, 0.76f, 0.67f);
        private readonly Color futureColor = new(0.54f, 0.65f, 0.66f);

        public void Configure(FishLanternExperienceController owner, DesktopXrInputRig input,
            Text source, Text[] frameText, Text title, Text body, Text tool, Text progress, Text notice,
            Text hint, Image[] fills, Text[] stageText, Image[] icons, Button start, Button next,
            Button restart, XRBaseInteractor[] hands, XRRayInteractor[] uiRays, Text[] allLabels)
        {
            experience = owner;
            desktopInput = input;
            instructionSource = source;
            frameSources = frameText;
            heading = title;
            instruction = body;
            heldTool = tool;
            progressLabel = progress;
            feedback = notice;
            inputHint = hint;
            stageFills = fills;
            stageLabels = stageText;
            stageIcons = icons;
            startButton = start;
            nextButton = next;
            restartButton = restart;
            handInteractors = hands;
            rays = uiRays;
            labels = allLabels;
            Refresh();
        }

        public void ConfigureReading(Text controls, Text buttonLabel, Button previous, Button next,
            Text pageLabel, Image noticeBackground)
        {
            controlsBody = controls;
            controlsButtonLabel = buttonLabel;
            previousReadingPage = previous;
            nextReadingPage = next;
            readingPageLabel = pageLabel;
            feedbackBackground = noticeBackground;
            lastReadingContent = null;
            Refresh();
        }

        public void ConfigureButtonReadability(Button[] buttons)
        {
            readableButtons = buttons ?? Array.Empty<Button>();
            readableButtonLabels = new Text[readableButtons.Length];
            for (var index = 0; index < readableButtons.Length; index++)
                if (readableButtons[index] != null)
                    readableButtonLabels[index] = readableButtons[index].GetComponentInChildren<Text>(true);
            RefreshButtonReadability();
        }

        public void ToggleControls()
        {
            controlsExpanded = !controlsExpanded;
            readingPage = 0;
            lastReadingContent = null;
            Refresh();
        }

        public void ConfigureControlArtwork(Button[] buttons, Image[] icons, int pauseIndex, Sprite pause, Sprite resume)
        {
            artworkButtons = buttons ?? Array.Empty<Button>();
            buttonIcons = icons ?? Array.Empty<Image>();
            pauseArtworkIndex = pauseIndex;
            pauseArtwork = pause;
            resumeArtwork = resume;
            RefreshButtonReadability();
        }

        public void ShowPreviousPage()
        {
            readingPage = Mathf.Max(0, readingPage - 1);
            Refresh();
        }

        public void ShowNextPage()
        {
            readingPage = Mathf.Min(Mathf.Max(0, readingPages.Count - 1), readingPage + 1);
            Refresh();
        }

        private void Awake()
        {
            runtimeFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 40);
            foreach (var label in labels) if (label != null) label.font = runtimeFont;
            lastReadingContent = null;
        }

        private void OnEnable() => nextRefresh = 0f;
        private void OnDestroy()
        {
            ((IDisposable)readingLayout).Dispose();
            if (runtimeFont != null) Destroy(runtimeFont);
        }

        private void LateUpdate()
        {
            // The authoritative controller refreshes first. A modest presentation cadence also
            // catches its pause/recovery state without duplicating its event or completion rules.
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.08f;
            Refresh();
        }

        public void Refresh()
        {
            if (experience == null || instructionSource == null) return;
            var step = experience.CurrentStep;
            var stageIndex = step switch
            {
                FishLanternStep.FrameMaking => 0,
                FishLanternStep.Papering => 1,
                FishLanternStep.SketchAndPaint => 2,
                FishLanternStep.LampDisplay => 3,
                FishLanternStep.Complete => 4,
                _ => -1
            };
            var progress = Mathf.Clamp01(experience.CurrentStageProgress);
            for (var index = 0; index < stageFills.Length; index++)
            {
                var color = index < stageIndex ? completedColor : index == stageIndex ? activeColor : futureColor;
                if (stageFills[index] != null)
                {
                    stageFills[index].fillAmount = index < stageIndex ? 1f : index == stageIndex ? progress : 0f;
                    stageFills[index].color = color;
                }
                if (index < stageLabels.Length && stageLabels[index] != null)
                {
                    stageLabels[index].color = color;
                    if (index < StageNames.Length) SetText(stageLabels[index], StageNames[index]);
                }
                if (index < stageIcons.Length && stageIcons[index] != null) stageIcons[index].color = color;
            }

            var raw = instructionSource.text ?? string.Empty;
            var noticeIndex = raw.IndexOf("\n\n", StringComparison.Ordinal);
            var body = noticeIndex < 0 ? raw : raw.Substring(0, noticeIndex);
            var notice = noticeIndex < 0 ? string.Empty : raw.Substring(noticeIndex + 2);
            var firstLine = body.IndexOf('\n');
            SetText(heading, firstLine < 0 ? body : body.Substring(0, firstLine));
            body = firstLine < 0 ? string.Empty : body.Substring(firstLine + 1);
            if (step == FishLanternStep.FrameMaking && progress < 1f && frameSources.Length >= 3)
                body = Read(frameSources[0]) + "\n" + Read(frameSources[1]) + "\n" + Read(frameSources[2]);
            SetText(progressLabel, step == FishLanternStep.Welcome ? "准备就绪 · 从扎骨架开始" :
                step == FishLanternStep.Complete ? "四个阶段已完成 · 可继续持杆展示" :
                step == FishLanternStep.FrameMaking && frameSources.Length > 3
                    ? Read(frameSources[3]) + $"  ·  当前阶段 {Mathf.RoundToInt(progress * 100f)}%"
                    : $"当前阶段进度  {Mathf.RoundToInt(progress * 100f)}%  ·  完成后进入下一阶段");

            // The old frame canvas is visually consolidated, so retain its specific failure
            // reason as well as the experience controller's general recovery notices.
            if (string.IsNullOrEmpty(notice) && step == FishLanternStep.FrameMaking && frameSources.Length > 4)
            {
                var frameNotice = Read(frameSources[4]);
                if (frameNotice.StartsWith("⚠", StringComparison.Ordinal)) notice = frameNotice;
            }
            if (experience.IsRestarting) notice = "正在恢复材料与工具，请稍候。";
            else if (experience.IsRecoveringTools) notice = "正在找回未安装物品；已完成作品保持不变。";
            else if (experience.IsPaused && string.IsNullOrEmpty(notice))
                notice = "已暂停 · 作品与制作进度已保留；点击“继续制作”或“继续展示”恢复。";
            else if (string.IsNullOrEmpty(notice)) notice = step == FishLanternStep.Complete
                ? "找回工具可将作品放回台面；重新开始会清除本轮制作。"
                : "工具掉落时使用“找回工具”；暂停后仍可查看当前提示。";
            // Keep the complete source notice available even when its compact summary requires
            // more than the two reserved lines. Pagination never changes the underlying operation.
            var feedbackSummary = notice;
            if (feedback != null && !Fits(feedback, notice))
            {
                body += "\n\n当前提示：\n" + notice;
                var sentenceEnd = notice.IndexOf('。');
                feedbackSummary = sentenceEnd >= 0 ? notice.Substring(0, sentenceEnd + 1) : "当前操作有补充提示。";
                if (!Fits(feedback, feedbackSummary)) feedbackSummary = "当前操作有补充提示。";
                feedbackSummary += " 完整内容请翻阅正文。";
                if (!Fits(feedback, feedbackSummary)) feedbackSummary = "当前操作有补充提示，请翻阅正文查看完整内容。";
            }
            SetText(feedback, feedbackSummary);
            if (feedback != null) feedback.color = experience.IsPaused ? activeColor : new Color(0.77f, 0.86f, 0.84f);

            var readingTarget = controlsExpanded && controlsBody != null ? controlsBody : instruction;
            var readingContent = controlsExpanded
                ? DesktopXrInputRig.ControlsText + "\n\nF5 重新开始 · F6 暂停或继续 · F7 找回工具\nEsc 退出体验\n操作空间按钮时，请先松开手中的工具，或改用空闲手。"
                : body;
            RefreshReading(readingTarget, readingContent);
            if (instruction != null) instruction.enabled = !controlsExpanded;
            if (controlsBody != null) controlsBody.enabled = controlsExpanded;
            if (heldTool != null) heldTool.enabled = !controlsExpanded;
            if (progressLabel != null) progressLabel.enabled = !controlsExpanded;
            if (feedback != null) feedback.enabled = !controlsExpanded;
            if (feedbackBackground != null) feedbackBackground.enabled = !controlsExpanded;
            SetText(controlsButtonLabel, controlsExpanded ? "关闭说明" : "操作说明");

            SetText(heldTool, BuildHeldToolText());
            SetText(inputHint, BuildInputHint());
            var busy = experience.IsRestarting || experience.IsRecoveringTools || experience.IsAdjustingPainting;
            if (startButton != null) startButton.interactable = step == FishLanternStep.Welcome && !busy;
            // Preserve the controller's eligibility. These two terminal states have no next stage.
            if (nextButton != null && step is FishLanternStep.Welcome or FishLanternStep.Complete) nextButton.interactable = false;
            if (restartButton != null) restartButton.interactable = step != FishLanternStep.Welcome && !busy;
            RefreshButtonReadability();
        }

        private void RefreshButtonReadability()
        {
            var enabledColor = new Color(0.94f, 0.96f, 0.93f);
            var disabledColor = new Color(0.40f, 0.49f, 0.51f);
            for (var index = 0; index < readableButtons.Length && index < readableButtonLabels.Length; index++)
            {
                var button = readableButtons[index];
                var label = readableButtonLabels[index];
                if (button != null && label != null)
                    label.color = button.IsInteractable() ? enabledColor : disabledColor;
            }
            for (var index = 0; index < artworkButtons.Length && index < buttonIcons.Length; index++)
                if (artworkButtons[index] != null && buttonIcons[index] != null)
                    buttonIcons[index].color = artworkButtons[index].IsInteractable() ? enabledColor : disabledColor;
            if (pauseArtworkIndex >= 0 && pauseArtworkIndex < buttonIcons.Length &&
                buttonIcons[pauseArtworkIndex] != null && pauseArtwork != null && resumeArtwork != null)
                buttonIcons[pauseArtworkIndex].sprite = experience != null && experience.IsPaused ? resumeArtwork : pauseArtwork;
        }

        private bool Fits(Text target, string value)
        {
            if (target == null || target.font == null) return true;
            var settings = target.GetGenerationSettings(new Vector2(target.GetPixelAdjustedRect().width, 0f));
            settings.resizeTextForBestFit = false;
            var height = readingLayout.GetPreferredHeight(value, settings) / target.pixelsPerUnit;
            return height <= target.GetPixelAdjustedRect().height - 4f;
        }

        private void RefreshReading(Text target, string content)
        {
            if (target == null) return;
            var size = target.GetPixelAdjustedRect().size;
            if (readingPages.Count == 0 || content != lastReadingContent || target != lastReadingTarget || size != lastReadingSize)
            {
                lastReadingContent = content;
                lastReadingTarget = target;
                lastReadingSize = size;
                readingPage = 0;
                readingPages.Clear();
                var remaining = content ?? string.Empty;
                while (remaining.Length > 0)
                {
                    if (Fits(target, remaining))
                    {
                        readingPages.Add(remaining);
                        break;
                    }
                    var lower = 1;
                    var upper = remaining.Length;
                    var fittingLength = 1;
                    while (lower <= upper)
                    {
                        var middle = (lower + upper) / 2;
                        if (Fits(target, remaining.Substring(0, middle)))
                        {
                            fittingLength = middle;
                            lower = middle + 1;
                        }
                        else upper = middle - 1;
                    }
                    // Prefer a complete source sentence or paragraph near the fitted boundary.
                    // Every source character remains on exactly one page; no ellipsis is introduced.
                    for (var index = fittingLength - 1; index >= fittingLength / 2; index--)
                    {
                        if (remaining[index] is not ('\n' or '。' or '；')) continue;
                        fittingLength = index + 1;
                        break;
                    }
                    if (fittingLength > 1 && char.IsHighSurrogate(remaining[fittingLength - 1])) fittingLength--;
                    readingPages.Add(remaining.Substring(0, fittingLength));
                    remaining = remaining.Substring(fittingLength);
                }
                if (readingPages.Count == 0) readingPages.Add(string.Empty);
            }
            readingPage = Mathf.Clamp(readingPage, 0, readingPages.Count - 1);
            SetText(target, readingPages[readingPage]);
            if (previousReadingPage != null) previousReadingPage.interactable = readingPage > 0;
            if (nextReadingPage != null) nextReadingPage.interactable = readingPage + 1 < readingPages.Count;
            SetText(readingPageLabel, (controlsExpanded ? "操作说明 " : "工序提示 ") + $"{readingPage + 1} / {readingPages.Count}");
        }

        private string BuildHeldToolText()
        {
            InteractableIdentity first = null;
            InteractableIdentity second = null;
            foreach (var hand in handInteractors)
            {
                if (hand == null) continue;
                foreach (var selected in hand.interactablesSelected)
                {
                    var identity = selected.transform.GetComponent<InteractableIdentity>();
                    if (identity == null || identity == first || identity == second) continue;
                    if (first == null) first = identity;
                    else if (second == null) second = identity;
                }
            }
            if (first == null) return "当前手持  ·  双手空闲";
            return "当前手持  ·  " + first.DisplayName + (second != null ? "  /  " + second.DisplayName : string.Empty);
        }

        private string BuildInputHint()
        {
            foreach (var ray in rays)
            {
                if (ray == null || !ray.TryGetCurrentUIRaycastResult(out var hit) || hit.gameObject == null) continue;
                var button = hit.gameObject.GetComponentInParent<Button>();
                if (button == null || !button.IsInteractable()) continue;
                var label = button.GetComponentInChildren<Text>();
                if (desktopInput != null && desktopInput.IsSimulationActive)
                    return "射线已对准“" + (label != null ? label.text : "按钮") + "” · 按 T 压下，再按 T 松开确认";
                return "射线已对准“" + (label != null ? label.text : "按钮") + "” · 轻按并松开扳机确认";
            }
            if (desktopInput == null || !desktopInput.IsSimulationActive)
                return "空闲手指向按钮 · 轻按并松开扳机确认；握持工具的手先松开抓取";
            var selected = desktopInput.CurrentObject switch
            {
                SimulatedTrackedObject.LeftHand => "左手",
                SimulatedTrackedObject.RightHand => "右手",
                _ => "头部"
            };
            return "电脑模拟 · 当前控制" + selected + " · 完整按键见“操作说明”";
        }

        private static string Read(Text source) => source != null ? source.text : string.Empty;
        private static void SetText(Text target, string value)
        {
            if (target != null && target.text != value) target.text = value;
        }
    }
}
