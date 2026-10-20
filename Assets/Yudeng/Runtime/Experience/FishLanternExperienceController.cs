using System.Collections;
using System.Collections.Generic;
using FishLantern.XR;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Yudeng.Input;
using Yudeng.Presentation;

namespace Yudeng.Experience
{
    /// <summary>Owns the production experience while the imported interaction layer reports physical actions.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-900)]
    public sealed class FishLanternExperienceController : MonoBehaviour, IInteractionPermissionService
    {
        [SerializeField] private GameStateManagerAdapter adapter;
        [SerializeField] private InteractionServiceHub hub;
        [SerializeField] private Transform experienceRoot;
        [SerializeField] private StageProgressController[] stages;
        [SerializeField] private DesktopXrInputRig desktopInput;
        [SerializeField] private Text statusText;
        [SerializeField] private Image progressFill;
        [SerializeField] private Button nextButton;
        [SerializeField] private Button pauseButton;
        [SerializeField] private Text pauseButtonLabel;
        [SerializeField] private Button recoverToolsButton;
        [SerializeField] private GrabInteractableEx frameBamboo;
        [SerializeField] private InteractableIdentity frameFixationZone;
        [SerializeField] private PaperingGuide paperingGuide;
        [SerializeField] private SketchAndPaintGuide sketchAndPaintGuide;
        [SerializeField] private Transform paintingFrame;
        [SerializeField] private Transform paintingTurntableCenter;
        [SerializeField] private Button rotatePaintingButton;
        [SerializeField] private Button resetDrawingButton;
        [SerializeField] private LampDisplayPresenter lampDisplay;
        [SerializeField] private InteractableIdentity displayRod;
        [SerializeField] private RodShakeDetector displayShake;
        [SerializeField] private FishLanternDisplayMotion displayMotion;
        [SerializeField] private Button installLampButton;
        [SerializeField] private Button switchLampButton;

        private MonoBehaviour[] interactionComponents;
        private GrabInteractableEx[] grabbables;
        private Coroutine restartRoutine;
        private Coroutine recoveryRoutine;
        private Coroutine paintingAdjustmentRoutine;
        private readonly Dictionary<string, GrabInteractableEx> grabbablesById = new();
        private FishLanternStep pendingStep = FishLanternStep.None;
        private bool refreshRequested;
        private bool previousInputPaused;
        private string notice;

        public FishLanternStep CurrentStep => adapter != null ? adapter.CurrentStep : FishLanternStep.None;
        public bool IsRestarting { get; private set; }
        public bool IsPaused { get; private set; }
        public bool IsRecoveringTools { get; private set; }
        public bool IsAdjustingPainting { get; private set; }
        public float CurrentStageProgress => CurrentStep == FishLanternStep.SketchAndPaint && sketchAndPaintGuide != null
            ? sketchAndPaintGuide.OverallProgress : CurrentStep == FishLanternStep.LampDisplay && lampDisplay != null
                ? ((lampDisplay.IsInstalled ? 1f : 0f) + (lampDisplay.IsLightOn ? 1f : 0f) +
                    (displayShake != null ? displayShake.Progress : 0f)) / 3f
                : FindCurrentStage() is { } stage ? stage.Progress : CurrentStep == FishLanternStep.Complete ? 1f : 0f;
        private bool IsTemporarilyBusy => IsRecoveringTools || IsAdjustingPainting;

        public void Configure(GameStateManagerAdapter stateAdapter, InteractionServiceHub serviceHub,
            Transform contentRoot, StageProgressController[] progressControllers, DesktopXrInputRig input)
        {
            Unsubscribe();
            adapter = stateAdapter;
            hub = serviceHub;
            experienceRoot = contentRoot;
            stages = progressControllers;
            desktopInput = input;
            interactionComponents = null;
            grabbables = null;
            BindServices();
            if (Application.isPlaying) CacheInteractions();
            if (isActiveAndEnabled) Subscribe();
            RefreshPresentation();
        }

        public void ConfigurePresentation(Text status, Image progress, Button next)
        {
            statusText = status;
            progressFill = progress;
            nextButton = next;
            RefreshPresentation();
        }

        public void ConfigureRecoveryPresentation(Button pause, Text pauseLabel, Button recover)
        {
            pauseButton = pause;
            pauseButtonLabel = pauseLabel;
            recoverToolsButton = recover;
            RefreshPresentation();
        }

        public void ConfigureFrameRecovery(GrabInteractableEx bamboo, InteractableIdentity fixationZone)
        {
            frameBamboo = bamboo;
            frameFixationZone = fixationZone;
        }

        public void ConfigurePaperingGuide(PaperingGuide guide)
        {
            if (paperingGuide != null) paperingGuide.InstructionChanged -= HandlePaperingInstructionChanged;
            paperingGuide = guide;
            if (isActiveAndEnabled && paperingGuide != null)
                paperingGuide.InstructionChanged += HandlePaperingInstructionChanged;
            RefreshPresentation();
        }

        public void ConfigureSketchAndPaintGuide(SketchAndPaintGuide guide)
        {
            if (sketchAndPaintGuide != null) sketchAndPaintGuide.InstructionChanged -= HandlePaintingInstructionChanged;
            sketchAndPaintGuide = guide;
            if (isActiveAndEnabled && sketchAndPaintGuide != null)
                sketchAndPaintGuide.InstructionChanged += HandlePaintingInstructionChanged;
            RefreshPresentation();
        }

        public void ConfigurePaintingTurntable(Transform frame, Transform center)
        {
            paintingFrame = frame;
            paintingTurntableCenter = center;
        }

        public void ConfigurePaintingRecoveryPresentation(Button rotate, Button resetDrawing)
        {
            rotatePaintingButton = rotate;
            resetDrawingButton = resetDrawing;
            RefreshPresentation();
        }

        public void ConfigureLampDisplay(LampDisplayPresenter presenter, InteractableIdentity rod,
            RodShakeDetector shake, Button installButton, Button lightButton, FishLanternDisplayMotion motion)
        {
            if (lampDisplay != null) lampDisplay.StateChanged -= HandleLampStateChanged;
            lampDisplay = presenter;
            displayRod = rod;
            displayShake = shake;
            displayMotion = motion;
            installLampButton = installButton;
            switchLampButton = lightButton;
            if (isActiveAndEnabled && lampDisplay != null) lampDisplay.StateChanged += HandleLampStateChanged;
            RefreshPresentation();
        }

        private void Awake()
        {
            BindServices();
            CacheInteractions();
        }

        private void OnEnable() => Subscribe();

        private void OnDisable()
        {
            Unsubscribe();
            if (restartRoutine != null) StopCoroutine(restartRoutine);
            if (recoveryRoutine != null) StopCoroutine(recoveryRoutine);
            if (paintingAdjustmentRoutine != null) StopCoroutine(paintingAdjustmentRoutine);
            restartRoutine = null;
            recoveryRoutine = null;
            paintingAdjustmentRoutine = null;
            if (IsRestarting && desktopInput != null) desktopInput.InputPaused = previousInputPaused;
            IsRestarting = false;
            IsPaused = false;
            IsRecoveringTools = false;
            IsAdjustingPainting = false;
            SetObjectsSuspended(false);
        }

        private void Start() => RefreshPresentation();

        private void BindServices()
        {
            if (adapter == null || hub == null) return;
            adapter.ConfigurePermissionPolicy(this);
            hub.Configure(adapter, adapter, hub.FeedbackService as MonoBehaviour, adapter);
        }

        private void Subscribe()
        {
            if (lampDisplay != null)
            {
                lampDisplay.StateChanged -= HandleLampStateChanged;
                lampDisplay.StateChanged += HandleLampStateChanged;
            }
            if (paperingGuide != null)
            {
                paperingGuide.InstructionChanged -= HandlePaperingInstructionChanged;
                paperingGuide.InstructionChanged += HandlePaperingInstructionChanged;
            }
            if (sketchAndPaintGuide != null)
            {
                sketchAndPaintGuide.InstructionChanged -= HandlePaintingInstructionChanged;
                sketchAndPaintGuide.InstructionChanged += HandlePaintingInstructionChanged;
            }
            if (adapter == null) return;
            adapter.InteractionReported.RemoveListener(HandleInteraction);
            adapter.InteractionReported.AddListener(HandleInteraction);
            adapter.StepChanged.RemoveListener(HandleStepChanged);
            adapter.StepChanged.AddListener(HandleStepChanged);
            adapter.TutorialPromptRequested.RemoveListener(HandlePrompt);
            adapter.TutorialPromptRequested.AddListener(HandlePrompt);
        }

        private void Unsubscribe()
        {
            if (lampDisplay != null) lampDisplay.StateChanged -= HandleLampStateChanged;
            if (paperingGuide != null) paperingGuide.InstructionChanged -= HandlePaperingInstructionChanged;
            if (sketchAndPaintGuide != null) sketchAndPaintGuide.InstructionChanged -= HandlePaintingInstructionChanged;
            if (adapter == null) return;
            adapter.InteractionReported.RemoveListener(HandleInteraction);
            adapter.StepChanged.RemoveListener(HandleStepChanged);
            adapter.TutorialPromptRequested.RemoveListener(HandlePrompt);
        }

        private void CacheInteractions()
        {
            if (experienceRoot == null) return;
            // XRI temporarily reparents held objects outside the content root. Keep the initial
            // references so a held tool and its crafting components are still recoverable.
            var knownComponents = new List<MonoBehaviour>();
            if (interactionComponents != null)
                foreach (var component in interactionComponents)
                    if (component != null) knownComponents.Add(component);
            foreach (var component in experienceRoot.GetComponentsInChildren<MonoBehaviour>(true))
                if (component != null && !knownComponents.Contains(component)) knownComponents.Add(component);
            interactionComponents = knownComponents.ToArray();
            var knownGrabbables = new List<GrabInteractableEx>();
            foreach (var component in interactionComponents)
                if (component is GrabInteractableEx item) knownGrabbables.Add(item);
            grabbables = knownGrabbables.ToArray();
            grabbablesById.Clear();
            foreach (var item in grabbables)
            {
                item.CaptureInitialPose();
                var identity = item.GetComponent<InteractableIdentity>();
                if (identity != null) grabbablesById[identity.StableId] = item;
            }
        }

        public bool CanInteract(InteractionRequest request, out InteractionFailureReason reason)
        {
            reason = InteractionFailureReason.None;
            if (request.InteractionKind == InteractionKind.Exit) return true;
            if (IsRestarting || CurrentStep == FishLanternStep.None)
            {
                reason = InteractionFailureReason.PrerequisiteMissing;
                return false;
            }
            if (request.InteractionKind == InteractionKind.Restart) return true;
            if (IsPaused || IsTemporarilyBusy || !Application.isFocused)
            {
                // XRI rechecks selection permission every frame, including existing sockets.
                if (request.InteractionKind == InteractionKind.Socketed &&
                    grabbablesById.TryGetValue(request.ObjectId, out var item) &&
                    item != null && item.IsSelectedBySocket(request.TargetId)) return true;
                reason = InteractionFailureReason.Cancelled;
                return false;
            }
            if (request.ObjectType == InteractableObjectType.WoodenRod &&
                (displayRod == null || request.ObjectId != displayRod.StableId ||
                 CurrentStep is not (FishLanternStep.LampDisplay or FishLanternStep.Complete) ||
                 lampDisplay == null || !lampDisplay.IsInstalled))
            {
                reason = InteractionFailureReason.PrerequisiteMissing;
                return false;
            }
            if (CurrentStep == FishLanternStep.Complete && request.RequiredStep != FishLanternStep.None)
            {
                var viewingRod = displayRod != null && request.ObjectId == displayRod.StableId &&
                    request.InteractionKind is InteractionKind.Grab or InteractionKind.Release or InteractionKind.Activate;
                if (!viewingRod)
                {
                    reason = InteractionFailureReason.WrongStep;
                    return false;
                }
            }
            if (request.InteractionKind == InteractionKind.Start && CurrentStep != FishLanternStep.Welcome)
            {
                reason = InteractionFailureReason.WrongStep;
                return false;
            }
            if (request.InteractionKind == InteractionKind.Next && !CurrentStageCompleted())
            {
                reason = InteractionFailureReason.PrerequisiteMissing;
                return false;
            }
            return true;
        }

        private void HandleInteraction(InteractionEventData data)
        {
            if (data == null) return;
            refreshRequested = true;
            if (data.Result != InteractionResult.Succeeded || IsRestarting) return;
            if (data.InteractionKind == InteractionKind.Restart)
            {
                RestartExperience();
                return;
            }
            if (data.InteractionKind == InteractionKind.Exit)
            {
                ExitExperience();
                return;
            }
            if (IsPaused || IsTemporarilyBusy || !Application.isFocused) return;
            switch (data.InteractionKind)
            {
                case InteractionKind.Start: StartExperience(); break;
                case InteractionKind.Next: RequestNextStep(); break;
                case InteractionKind.TapeFixed:
                    if (data.Step == FishLanternStep.FrameMaking && frameBamboo != null &&
                        frameFixationZone != null && data.ObjectId == frameFixationZone.StableId)
                        frameBamboo.MarkFixedInPlace();
                    break;
                case InteractionKind.StepComplete:
                    var stage = FindCurrentStage();
                    if (stage != null && stage.IsComplete && stage.RequirementCount > 0 &&
                        data.Step == CurrentStep && stage.GetComponent<InteractableIdentity>().StableId == data.ObjectId)
                    {
                        if (CurrentStep == FishLanternStep.FrameMaking)
                            notice = "骨架制作完成，可查看连接效果，点击“下一步”开始糊纸。";
                        else pendingStep = FollowingStep(CurrentStep);
                    }
                    break;
            }
        }

        public void StartExperience()
        {
            if (!IsRestarting && !IsPaused && !IsTemporarilyBusy && Application.isFocused && CurrentStep == FishLanternStep.Welcome)
                pendingStep = FishLanternStep.FrameMaking;
        }

        public void RequestNextStep()
        {
            if (IsRestarting || IsPaused || IsTemporarilyBusy || !Application.isFocused) return;
            if (CurrentStageCompleted()) pendingStep = FollowingStep(CurrentStep);
            else
            {
                notice = "请先完成本阶段的制作操作，再进入下一阶段。";
                refreshRequested = true;
            }
        }

        private void LateUpdate()
        {
            // Finish broadcasting the successful action before hiding its objects and entering the next stage.
            if (pendingStep != FishLanternStep.None && !IsRestarting && !IsPaused && !IsTemporarilyBusy && Application.isFocused)
            {
                var next = pendingStep;
                pendingStep = FishLanternStep.None;
                adapter.SetAuthoritativeStep(next);
            }
            if (refreshRequested) RefreshPresentation();
            var keyboard = Keyboard.current;
            if (keyboard == null || !Application.isFocused) return;
            if (keyboard.f5Key.wasPressedThisFrame) RestartExperience();
            if (keyboard.f6Key.wasPressedThisFrame) TogglePause();
            if (keyboard.f7Key.wasPressedThisFrame) RecoverLooseTools();
            if (keyboard.escapeKey.wasPressedThisFrame) ExitExperience();
        }

        public void TogglePause()
        {
            if (IsPaused) ResumeExperience();
            else PauseExperience();
        }

        public void PauseExperience()
        {
            if (IsPaused || IsRestarting || IsTemporarilyBusy || !IsCraftingStep()) return;
            IsPaused = true;
            CacheInteractions();
            ReleaseInputHands();
            SetObjectsSuspended(true);
            notice = CurrentStep switch
            {
                FishLanternStep.Papering => "制作已暂停，已裁开的轮廓、涂胶进度和已贴纸面均保留。点击“继续制作”后需重新握持工具。",
                FishLanternStep.SketchAndPaint => "制作已暂停，当前描线、笔触、颜色与已贴纸面均保留。点击“继续制作”后重新握持工具并接触纸面。",
                FishLanternStep.LampDisplay or FishLanternStep.Complete => "展示已暂停，灯光、纸面、笔触和已确认的摆动次数均保留。点击“继续展示”后重新握住木杆。",
                _ => "制作已暂停，已完成的工序保留。点击“继续制作”后需重新握持工具；未完成的持续按压需要重新操作。",
            };
            RefreshPresentation();
        }

        public void ResumeExperience()
        {
            if (!IsPaused || IsRestarting || IsTemporarilyBusy) return;
            // Keep the permission gate closed until every object's resting state is restored.
            SetObjectsSuspended(false);
            IsPaused = false;
            notice = "制作已继续，请重新握持工具，按当前工序提示操作。";
            RefreshPresentation();
        }

        public void RecoverLooseTools()
        {
            if (IsRestarting || IsTemporarilyBusy || !IsCraftingStep() || experienceRoot == null) return;
            recoveryRoutine = StartCoroutine(RecoverToolsRoutine());
        }

        private IEnumerator RecoverToolsRoutine()
        {
            IsRecoveringTools = true;
            CacheInteractions();
            notice = "正在找回未固定的工具和材料……已固定、已安装的构件将保留。";
            ReleaseInputHands();
            SetObjectsSuspended(true);
            RefreshPresentation();
            // Wait for XRI's deferred detach, then restore only the released physical objects.
            yield return null;
            yield return null;
            var recoveredCount = 0;
            foreach (var item in grabbables)
                if (item != null && item.isActiveAndEnabled && item.TryRecoverInitialPose()) recoveredCount++;
            if (CurrentStep is FishLanternStep.LampDisplay or FishLanternStep.Complete)
            {
                // The fixed frame's suspension otherwise reapplies its old held pose after recovery.
                // Permissions remain closed throughout this reposition, including a paused recovery.
                frameBamboo?.SetInteractionSuspended(false);
                displayMotion?.ReturnToWorkbench();
                frameBamboo?.SetInteractionSuspended(true);
            }
            Physics.SyncTransforms();
            // Consume contacts caused by repositioning while crafting permissions are still closed.
            yield return new WaitForFixedUpdate();
            if (!IsPaused) SetObjectsSuspended(false);
            IsRecoveringTools = false;
            recoveryRoutine = null;
            notice = recoveredCount > 0
                ? $"已将 {recoveredCount} 件未固定的工具和材料放回初始位置，制作成果保持不变。"
                : "没有需要找回的未固定工具或材料，制作成果保持不变。";
            if (IsPaused) notice += "制作仍处于暂停状态，继续后需重新握持工具。";
            RefreshPresentation();
        }

        private bool IsCraftingStep() => CurrentStep is FishLanternStep.FrameMaking or
            FishLanternStep.Papering or FishLanternStep.SketchAndPaint or FishLanternStep.LampDisplay or FishLanternStep.Complete;

        public void RotatePaintingFrame()
        {
            if (!CanAdjustPainting() || paintingFrame == null || paintingTurntableCenter == null) return;
            paintingAdjustmentRoutine = StartCoroutine(AdjustPaintingRoutine(rotateFrame: true));
        }

        public void ResetCurrentDrawingOperation()
        {
            if (!CanAdjustPainting() || sketchAndPaintGuide == null || !sketchAndPaintGuide.CanResetCurrentOperation)
                return;
            paintingAdjustmentRoutine = StartCoroutine(AdjustPaintingRoutine(rotateFrame: false));
        }

        private bool CanAdjustPainting() => CurrentStep == FishLanternStep.SketchAndPaint &&
            !IsRestarting && !IsPaused && !IsTemporarilyBusy;

        private IEnumerator AdjustPaintingRoutine(bool rotateFrame)
        {
            IsAdjustingPainting = true;
            CacheInteractions();
            notice = rotateFrame ? "正在转动鱼灯，已完成的绘画保留……" : "正在恢复当前尚未完成的绘画工序……";
            ReleaseInputHands();
            SetObjectsSuspended(true);
            RefreshPresentation();
            // Release XRI selections before changing a surface pose or its unfinished drawing.
            yield return null;
            yield return null;
            sketchAndPaintGuide?.ClearStrokeContinuity();
            // A suspended grab reapplies its captured pose every XRI update. Resume it while
            // permissions remain closed, then change the same assembled artifact's pose.
            SetObjectsSuspended(false);
            if (rotateFrame && paintingFrame != null && paintingTurntableCenter != null)
            {
                var center = paintingTurntableCenter.position;
                paintingFrame.RotateAround(center, Vector3.up, 90f);
                var body = paintingFrame.GetComponent<Rigidbody>();
                if (body != null)
                {
                    body.position = paintingFrame.position;
                    body.rotation = paintingFrame.rotation;
                }
                notice = "鱼灯已转动 90 度，纸面与笔触保持不变。请重新握持工具，继续当前纸面。";
            }
            else
            {
                var restored = sketchAndPaintGuide != null && sketchAndPaintGuide.ResetCurrentOperation();
                notice = restored
                    ? "已清除当前纸面尚未完成的绘画工序。已完成的描线、其他纸面、骨架与糊纸成果均保留。"
                    : "当前没有可恢复的未完成绘画工序。";
            }
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            sketchAndPaintGuide?.ClearStrokeContinuity();
            IsAdjustingPainting = false;
            paintingAdjustmentRoutine = null;
            RefreshPresentation();
        }

        private void ReleaseInputHands()
        {
            sketchAndPaintGuide?.ClearStrokeContinuity();
            displayShake?.ClearMotionContinuity();
            displayMotion?.ClearMotionContinuity();
            if (desktopInput == null) return;
            // Do not pause the device input: head tracking and UI trigger input remain available.
            desktopInput.ReleaseBothHands();
            desktopInput.PublishDeviceState();
        }

        private void SetObjectsSuspended(bool suspended)
        {
            displayMotion?.SetSuspended(suspended);
            displayShake?.ClearMotionContinuity();
            if (grabbables == null) return;
            foreach (var item in grabbables)
                if (item != null) item.SetInteractionSuspended(suspended);
        }

        public void RestartExperience()
        {
            if (!IsRestarting && adapter != null && experienceRoot != null)
            {
                if (recoveryRoutine != null) StopCoroutine(recoveryRoutine);
                if (paintingAdjustmentRoutine != null) StopCoroutine(paintingAdjustmentRoutine);
                recoveryRoutine = null;
                paintingAdjustmentRoutine = null;
                IsRecoveringTools = false;
                IsAdjustingPainting = false;
                IsPaused = false;
                SetObjectsSuspended(false);
                restartRoutine = StartCoroutine(RestartRoutine());
            }
        }

        private IEnumerator RestartRoutine()
        {
            IsRestarting = true;
            pendingStep = FishLanternStep.None;
            notice = "正在归还工具并恢复制作材料……";
            if (desktopInput != null)
            {
                previousInputPaused = desktopInput.InputPaused;
                desktopInput.InputPaused = true;
                desktopInput.ReleaseBothHands();
                desktopInput.PublishDeviceState();
            }
            CacheInteractions();
            ReleaseGrabs();
            adapter.SetAuthoritativeStep(FishLanternStep.None);
            RefreshPresentation();
            // Let XRI finish its deferred detach before restoring any Rigidbody pose.
            yield return null;
            yield return null;
            foreach (var component in interactionComponents)
                if (component != null && component is IInteractionResettable resettable)
                    resettable.ResetInteraction();
            if (hub.FeedbackService is IInteractionResettable feedback) feedback.ResetInteraction();
            Physics.SyncTransforms();
            if (desktopInput != null)
            {
                desktopInput.RequestSimulationReset();
                while (desktopInput != null && desktopInput.IsSimulationResetPending) yield return null;
                if (desktopInput != null) desktopInput.InputPaused = previousInputPaused;
            }
            IsRestarting = false;
            restartRoutine = null;
            adapter.SetAuthoritativeStep(FishLanternStep.Welcome);
            RefreshPresentation();
        }

        private void ReleaseGrabs()
        {
            if (grabbables == null) return;
            foreach (var item in grabbables)
                if (item != null && item.isSelected && item.interactionManager != null)
                    item.interactionManager.CancelInteractableSelection((IXRSelectInteractable)item);
        }

        public void ExitExperience()
        {
            pendingStep = FishLanternStep.None;
            if (recoveryRoutine != null) StopCoroutine(recoveryRoutine);
            if (paintingAdjustmentRoutine != null) StopCoroutine(paintingAdjustmentRoutine);
            recoveryRoutine = null;
            paintingAdjustmentRoutine = null;
            IsRecoveringTools = false;
            IsAdjustingPainting = false;
            if (desktopInput != null)
            {
                desktopInput.InputPaused = true;
                desktopInput.ReleaseBothHands();
                desktopInput.PublishDeviceState();
            }
            ReleaseGrabs();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private StageProgressController FindCurrentStage()
        {
            if (stages == null) return null;
            foreach (var stage in stages)
                if (stage != null && stage.Stage == CurrentStep) return stage;
            return null;
        }

        private bool CurrentStageCompleted() => FindCurrentStage() is { IsComplete: true, RequirementCount: > 0 };

        private static FishLanternStep FollowingStep(FishLanternStep step) => step switch
        {
            FishLanternStep.FrameMaking => FishLanternStep.Papering,
            FishLanternStep.Papering => FishLanternStep.SketchAndPaint,
            FishLanternStep.SketchAndPaint => FishLanternStep.LampDisplay,
            FishLanternStep.LampDisplay => FishLanternStep.Complete,
            _ => FishLanternStep.None,
        };

        private void HandleStepChanged(FishLanternStep step)
        {
            displayShake?.ClearMotionContinuity();
            notice = string.Empty;
            refreshRequested = true;
        }

        private void HandleLampStateChanged()
        {
            if (CurrentStep is not (FishLanternStep.LampDisplay or FishLanternStep.Complete)) return;
            if (!IsPaused && !IsTemporarilyBusy && !IsRestarting) notice = string.Empty;
            refreshRequested = true;
        }

        private void OnApplicationFocus(bool focused)
        {
            refreshRequested = true;
            if (focused) return;
            displayShake?.ClearMotionContinuity();
            displayMotion?.ClearMotionContinuity();
            if (Application.isPlaying && IsCraftingStep()) PauseExperience();
        }

        private void HandlePaperingInstructionChanged()
        {
            if (CurrentStep != FishLanternStep.Papering) return;
            if (!IsPaused && !IsTemporarilyBusy && !IsRestarting) notice = string.Empty;
            refreshRequested = true;
        }

        private void HandlePaintingInstructionChanged()
        {
            if (CurrentStep != FishLanternStep.SketchAndPaint) return;
            if (!IsPaused && !IsTemporarilyBusy && !IsRestarting) notice = string.Empty;
            refreshRequested = true;
        }

        private void HandlePrompt(TutorialPromptRequest prompt)
        {
            if (IsPaused || IsTemporarilyBusy || IsRestarting) return;
            notice = prompt.Reason switch
            {
                InteractionFailureReason.WrongStep => "当前阶段暂时不能操作这件物品，请按制作提示继续。",
                InteractionFailureReason.WrongObject => "请拿取当前步骤所需的工具或材料。",
                InteractionFailureReason.WrongHand => "请使用提示的手抓取物品。",
                InteractionFailureReason.PrerequisiteMissing => "请先完成前面的制作操作。",
                InteractionFailureReason.Misaligned => "请按顺序接触标记点，并对准目标位置。",
                InteractionFailureReason.AlreadyCompleted => "这一步已经完成，请继续后面的操作。",
                _ => "请按当前制作提示操作。",
            };
            refreshRequested = true;
        }

        private void RefreshPresentation()
        {
            refreshRequested = false;
            if (progressFill != null) progressFill.fillAmount = CurrentStageProgress;
            if (nextButton != null)
                nextButton.interactable = !IsRestarting && !IsPaused && !IsTemporarilyBusy && CurrentStageCompleted();
            if (pauseButton != null)
                pauseButton.interactable = !IsRestarting && !IsTemporarilyBusy && IsCraftingStep();
            var displayStep = CurrentStep is FishLanternStep.LampDisplay or FishLanternStep.Complete;
            if (pauseButtonLabel != null) pauseButtonLabel.text = displayStep
                ? IsPaused ? "继续展示" : "暂停展示" : IsPaused ? "继续制作" : "暂停制作";
            if (recoverToolsButton != null)
                recoverToolsButton.interactable = !IsRestarting && !IsTemporarilyBusy && IsCraftingStep();
            if (rotatePaintingButton != null)
            {
                rotatePaintingButton.gameObject.SetActive(!displayStep);
                rotatePaintingButton.interactable = CanAdjustPainting() && paintingFrame != null && paintingTurntableCenter != null;
            }
            if (resetDrawingButton != null)
            {
                resetDrawingButton.gameObject.SetActive(!displayStep);
                resetDrawingButton.interactable = CanAdjustPainting() && sketchAndPaintGuide != null &&
                    sketchAndPaintGuide.CanResetCurrentOperation;
            }
            var canUseLamp = CurrentStep == FishLanternStep.LampDisplay && !IsPaused &&
                !IsRestarting && !IsTemporarilyBusy && Application.isFocused && lampDisplay != null;
            if (installLampButton != null)
            {
                installLampButton.gameObject.SetActive(displayStep);
                installLampButton.interactable = canUseLamp && !lampDisplay.IsInstalled;
            }
            if (switchLampButton != null)
            {
                switchLampButton.gameObject.SetActive(displayStep);
                switchLampButton.interactable = canUseLamp && lampDisplay.IsInstalled && !lampDisplay.IsLightOn;
            }
            if (statusText == null) return;
            var instruction = CurrentStep switch
            {
                FishLanternStep.Welcome => "鱼灯制作工坊\n点击“开始”，依次完成扎骨架、糊纸、绘制和装灯。\n双手握持抓取，扳机操作工具；F5 重新开始，F6 暂停或继续，F7 找回工具。",
                FishLanternStep.FrameMaking when CurrentStageCompleted() => "第一阶段 · 骨架制作完成\n前段身体和六件鱼鳍已完成，现与预制鱼头、后段和鱼尾组合展示。\n点击“下一步”开始糊纸。",
                FishLanternStep.FrameMaking => "第一阶段 · 扎骨架\n双手握住长竹条并向内弯曲；握住胶带，按住扳机贴合固定区。\n随后按步骤面板完成标记、竹圈、短竹条和鱼鳍安装。",
                FishLanternStep.Papering => "第二阶段 · 糊纸\n" +
                    (paperingGuide != null && !string.IsNullOrEmpty(paperingGuide.CurrentInstruction)
                        ? paperingGuide.CurrentInstruction
                        : "沿纸样标记裁剪，再给骨架对应区域涂胶，最后拿起纸片对准同名位置贴合。"),
                FishLanternStep.SketchAndPaint => "第三阶段 · 绘制与上色\n" +
                    (sketchAndPaintGuide != null && !string.IsNullOrEmpty(sketchAndPaintGuide.CurrentInstruction)
                        ? sketchAndPaintGuide.CurrentInstruction
                        : "握住铅笔并按住扳机，沿纸面提示线移动；描线后让画笔蘸取颜料，再用笔尖涂过对应区域。"),
                FishLanternStep.LampDisplay => "第四阶段 · 装灯与展示\n" + BuildLampInstruction(),
                FishLanternStep.Complete => "鱼灯制作完成\n同一条鱼灯和本轮绘画已保留，可继续握杆轻晃，从两侧查看作品。\n“找回工具”可将木杆和作品放回台面；“重新开始”将清除本轮制作并恢复全部材料。",
                _ => "正在恢复制作工坊……",
            };
            statusText.text = string.IsNullOrEmpty(notice) ? instruction : instruction + "\n\n" + notice;
        }

        private string BuildLampInstruction()
        {
            if (lampDisplay == null || !lampDisplay.IsInstalled)
                return "第 1 / 3 步 · 安装灯具\n点击“安装灯具”，将预制灯具装入刚完成的鱼灯；本体验省略内部安装过程。\n安装后还需单独点击“点亮鱼灯”，作品不会提前亮起。";
            if (!lampDisplay.IsLightOn)
                return "第 2 / 3 步 · 点亮鱼灯\n灯具已经安装，纸面与实际笔触均已保留。\n点击“点亮鱼灯”打开暖光，再握住木杆轻晃。";
            var count = displayShake != null ? displayShake.CompletedSwingCount : 0;
            var required = displayShake != null ? displayShake.RequiredSwingCount : 4;
            return $"第 3 / 3 步 · 持杆展示（已确认 {count} / {required} 次往返）\n" +
                "握住木杆，在舒适范围内左右轻晃；每次移开后反向摆回，鱼灯各节会依次响应。\n" +
                "静止、只向一个方向移动和松手回位不会增加展示进度。";
        }
    }
}
