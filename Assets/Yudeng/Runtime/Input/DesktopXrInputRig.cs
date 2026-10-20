using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Management;

namespace Yudeng.Input
{
    public enum SimulatedTrackedObject { Head, LeftHand, RightHand }

    /// <summary>
    /// Supplies official Input System XR devices. The scene's ordinary tracked pose
    /// drivers and XRI interactors consume them exactly as they consume controllers.
    /// This component never moves scene hands or writes a crafting step.
    /// </summary>
    [DefaultExecutionOrder(-29900)]
    [DisallowMultipleComponent]
    public sealed class DesktopXrInputRig : MonoBehaviour
    {
        [Header("Initial tracking-space poses (metres)")]
        public Vector3 initialHeadPosition = new Vector3(0f, 1.6f, -0.65f);
        public Vector3 initialHeadEulerAngles = new Vector3(25f, 0f, 0f);
        public Vector3 initialLeftHandPosition = new Vector3(-0.23f, 0.95f, 0.18f);
        public Vector3 initialLeftHandEulerAngles;
        public Vector3 initialRightHandPosition = new Vector3(0.23f, 0.95f, 0.18f);
        public Vector3 initialRightHandEulerAngles;

        [Header("Desktop movement")]
        [Min(0.01f)] public float translationSpeed = 0.3f;
        [Min(1f)] public float rotationSpeed = 60f;
        [Min(0.001f)] public float translationTapStep = 0.01f;
        [Min(0.1f)] public float rotationTapStep = 3f;
        [Min(0.01f)] public float precisionMultiplier = 0.2f;

        public const string ControlsText =
            "F 切换行走／制作 · 行走时 W/A/S/D 移动，左右方向键转身\n" +
            "1 头部 · 2 左手 · 3 右手\n" +
            "W/S 前后 · A/D 左右 · Q/E 下降/上升\n" +
            "方向键 俯仰/转向 · Z/C 翻滚 · Shift 精细移动\n" +
            "G 握持开关 · T 扳机开关（空间按钮按下后须释放）\n" +
            "空格 释放当前手 · Backspace 释放双手 · Home 输入复位";

        public SimulatedTrackedObject CurrentObject { get; private set; } = SimulatedTrackedObject.LeftHand;
        public bool IsSimulationActive { get; private set; }
        public bool IsDeviceModeActive { get; private set; }
        public bool IsWalkingMode { get; private set; }
        public bool IsSimulationResetPending => resetRequestedFrame >= 0;
        public bool InputPaused { get; set; }
        public string StatusText { get; private set; } = "正在准备输入";

        public event Action InputStateChanged;

        readonly Pose[] poses = new Pose[3];
        readonly bool[] grips = new bool[3];
        readonly bool[] triggers = new bool[3];
        XRSimulatedHMD simulatedHead;
        XRSimulatedController simulatedLeftHand;
        XRSimulatedController simulatedRightHand;
        XRManagerSettings ownedDeviceManager;
        XRDisplaySubsystem startingDisplaySubsystem;
        bool simulationInitialized;
        Vector2 walkingInput;
        Vector2 turningInput;
        int resetRequestedFrame = -1;

        void OnEnable()
        {
            StartCoroutine(InitializeInput());
        }

        IEnumerator InitializeInput()
        {
            // Prefer the headset by default; desktop simulation remains an explicit option.
            if (HasCommandLineArgument("--desktop"))
            {
                StartSimulation();
                yield break;
            }

            StatusText = "正在启动真实设备输入";
            // XR Management requires graphics initialization and Start to finish.
            yield return null;
            if (TryStartDeviceMode())
            {
                float deadline = Time.realtimeSinceStartup + 3f;
                while (startingDisplaySubsystem != null && !startingDisplaySubsystem.running &&
                       Time.realtimeSinceStartup < deadline)
                    yield return null;

                if (startingDisplaySubsystem != null && startingDisplaySubsystem.running)
                {
                    IsDeviceModeActive = true;
                    StatusText = "真实设备模式（设备兼容性需要真机验证）";
                    InputStateChanged?.Invoke();
                    yield break;
                }

                Debug.LogWarning("真实设备显示未能运行，已回到电脑模拟。", this);
                StopOwnedDeviceMode();
            }

            StartSimulation();
            StatusText = "真实设备未能启动，已进入电脑模拟";
            InputStateChanged?.Invoke();
        }

        bool TryStartDeviceMode()
        {
            var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (manager == null)
                return false;

            try
            {
                if (manager.activeLoader == null)
                {
                    ownedDeviceManager = manager;
                    manager.InitializeLoaderSync();
                }
                if (manager.activeLoader == null)
                {
                    StopOwnedDeviceMode();
                    return false;
                }

                manager.StartSubsystems();
                startingDisplaySubsystem = manager.activeLoader.GetLoadedSubsystem<XRDisplaySubsystem>();
                StatusText = "正在等待头显显示启动";
                InputStateChanged?.Invoke();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("真实设备启动失败，将使用电脑模拟：" + exception.Message, this);
                StopOwnedDeviceMode();
                return false;
            }
        }

        /// <summary>Creates only this component's three simulated devices.</summary>
        public void StartSimulation()
        {
            if (IsSimulationActive)
                return;
            if (IsDeviceModeActive)
                throw new InvalidOperationException("Cannot add simulated devices while real XR input is active.");

            SimulatedInputLayoutLoader.Initialize();
            simulatedHead = InputSystem.AddDevice<XRSimulatedHMD>("Yudeng Simulated Head");
            simulatedLeftHand = InputSystem.AddDevice<XRSimulatedController>("Yudeng Simulated Left Hand");
            simulatedRightHand = InputSystem.AddDevice<XRSimulatedController>("Yudeng Simulated Right Hand");
            InputSystem.SetDeviceUsage(simulatedLeftHand, UnityEngine.InputSystem.CommonUsages.LeftHand);
            InputSystem.SetDeviceUsage(simulatedRightHand, UnityEngine.InputSystem.CommonUsages.RightHand);
            IsSimulationActive = true;
            StatusText = "电脑模拟模式";
            if (!simulationInitialized)
                ResetSimulation();
            PublishDeviceState();
            InputStateChanged?.Invoke();
        }

        void Update()
        {
            if (!IsSimulationActive)
                return;

            walkingInput = Vector2.zero;
            turningInput = Vector2.zero;

            if (IsSimulationResetPending)
            {
                // Keep a complete intervening frame with the old poses and released
                // controls so XRI can emit selectExited before any pose is restored.
                // No keyboard input can re-grab an object during this interval.
                if (Time.frameCount > resetRequestedFrame + 1)
                    ResetSimulation();
                PublishDeviceState();
                return;
            }

            if (!InputPaused && Application.isFocused)
                ReadKeyboard();

            // Follow the official simulator's InputState.Change path. It makes the
            // controls available before the ordinary pose drivers and XRI update.
            PublishDeviceState();
        }

        void ReadKeyboard()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            if (keyboard.digit1Key.wasPressedThisFrame) SelectObject(SimulatedTrackedObject.Head);
            if (keyboard.digit2Key.wasPressedThisFrame) SelectObject(SimulatedTrackedObject.LeftHand);
            if (keyboard.digit3Key.wasPressedThisFrame) SelectObject(SimulatedTrackedObject.RightHand);
            if (keyboard.homeKey.wasPressedThisFrame)
            {
                RequestSimulationReset();
                return;
            }
            if (keyboard.backspaceKey.wasPressedThisFrame) ReleaseBothHands();
            if (keyboard.spaceKey.wasPressedThisFrame) ReleaseCurrentHand();
            if (keyboard.fKey.wasPressedThisFrame)
            {
                IsWalkingMode = !IsWalkingMode;
                StatusText = IsWalkingMode ? "电脑模拟：行走模式（F 返回制作）" : "电脑模拟：制作模式（F 行走）";
                InputStateChanged?.Invoke();
            }
            if (CurrentObject != SimulatedTrackedObject.Head)
            {
                if (keyboard.gKey.wasPressedThisFrame) SetGrip(CurrentObject, !GetGrip(CurrentObject));
                if (keyboard.tKey.wasPressedThisFrame) SetTrigger(CurrentObject, !GetTrigger(CurrentObject));
            }

            if (IsWalkingMode)
            {
                // Publish ordinary controller sticks. The same XRI locomotion
                // providers move the origin and held objects in desktop and XR.
                walkingInput = Vector2.ClampMagnitude(new Vector2(
                    (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
                    (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f)), 1f);
                turningInput = new Vector2(
                    (keyboard.rightArrowKey.isPressed ? 1f : 0f) - (keyboard.leftArrowKey.isPressed ? 1f : 0f), 0f);
                return;
            }

            float precision = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed
                ? precisionMultiplier : 1f;
            float translation = translationSpeed * Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            float rotation = rotationSpeed * Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            var localMovement = new Vector3(
                KeyStep(keyboard.dKey, translation, translationTapStep) - KeyStep(keyboard.aKey, translation, translationTapStep),
                KeyStep(keyboard.eKey, translation, translationTapStep) - KeyStep(keyboard.qKey, translation, translationTapStep),
                KeyStep(keyboard.wKey, translation, translationTapStep) - KeyStep(keyboard.sKey, translation, translationTapStep));
            var eulerMovement = new Vector3(
                KeyStep(keyboard.downArrowKey, rotation, rotationTapStep) - KeyStep(keyboard.upArrowKey, rotation, rotationTapStep),
                KeyStep(keyboard.rightArrowKey, rotation, rotationTapStep) - KeyStep(keyboard.leftArrowKey, rotation, rotationTapStep),
                KeyStep(keyboard.zKey, rotation, rotationTapStep) - KeyStep(keyboard.cKey, rotation, rotationTapStep));
            if (localMovement == Vector3.zero && eulerMovement == Vector3.zero)
                return;

            var selectedPose = GetSimulatedPose(CurrentObject);
            // Translation follows the head's horizontal facing, so tilting a hand
            // never changes what forward means. Vertical motion stays world-up.
            var headHeading = Quaternion.Euler(0f, poses[0].rotation.eulerAngles.y, 0f);
            SetSimulatedPose(CurrentObject,
                selectedPose.position + headHeading * localMovement * precision,
                selectedPose.rotation * Quaternion.Euler(eulerMovement * precision));
        }

        static float KeyStep(KeyControl key, float continuousStep, float tapStep)
        {
            if (key.wasPressedThisFrame)
                return Mathf.Max(continuousStep, tapStep);
            return key.isPressed ? continuousStep : 0f;
        }

        public void SelectObject(SimulatedTrackedObject trackedObject)
        {
            ValidateObject(trackedObject);
            CurrentObject = trackedObject;
            InputStateChanged?.Invoke();
        }

        public Pose GetSimulatedPose(SimulatedTrackedObject trackedObject)
        {
            ValidateObject(trackedObject);
            return poses[(int)trackedObject];
        }

        public void SetSimulatedPose(SimulatedTrackedObject trackedObject, Vector3 position, Quaternion rotation)
        {
            ValidateObject(trackedObject);
            poses[(int)trackedObject] = new Pose(position, rotation.normalized);
            InputStateChanged?.Invoke();
        }

        public bool GetGrip(SimulatedTrackedObject trackedObject)
        {
            ValidateObject(trackedObject);
            return grips[(int)trackedObject];
        }

        public bool GetTrigger(SimulatedTrackedObject trackedObject)
        {
            ValidateObject(trackedObject);
            return triggers[(int)trackedObject];
        }

        public void SetGrip(SimulatedTrackedObject trackedObject, bool held)
        {
            ValidateObject(trackedObject);
            if (trackedObject == SimulatedTrackedObject.Head) return;
            grips[(int)trackedObject] = held;
            InputStateChanged?.Invoke();
        }

        public void SetTrigger(SimulatedTrackedObject trackedObject, bool held)
        {
            ValidateObject(trackedObject);
            if (trackedObject == SimulatedTrackedObject.Head) return;
            triggers[(int)trackedObject] = held;
            InputStateChanged?.Invoke();
        }

        public void ReleaseCurrentHand()
        {
            SetGrip(CurrentObject, false);
            SetTrigger(CurrentObject, false);
        }

        public void ReleaseBothHands()
        {
            Array.Clear(grips, 0, grips.Length);
            Array.Clear(triggers, 0, triggers.Length);
            InputStateChanged?.Invoke();
        }

        /// <summary>
        /// User-facing reset: releases first, then restores poses after XRI has had
        /// a full frame to process release. Repeated requests do not stack.
        /// </summary>
        public void RequestSimulationReset()
        {
            if (!IsSimulationActive || IsSimulationResetPending)
                return;
            ReleaseBothHands();
            PublishDeviceState();
            resetRequestedFrame = Time.frameCount;
        }

        /// <summary>
        /// Synchronous reset for initialization/tests or after object recovery has
        /// already released XRI selections. User controls use RequestSimulationReset.
        /// The crafting controller separately owns object recovery.
        /// </summary>
        public void ResetSimulation()
        {
            resetRequestedFrame = -1;
            walkingInput = Vector2.zero;
            turningInput = Vector2.zero;
            IsWalkingMode = false;
            if (IsSimulationActive) StatusText = "电脑模拟：制作模式（F 行走）";
            ReleaseBothHands();
            poses[0] = new Pose(initialHeadPosition, Quaternion.Euler(initialHeadEulerAngles));
            poses[1] = new Pose(initialLeftHandPosition, Quaternion.Euler(initialLeftHandEulerAngles));
            poses[2] = new Pose(initialRightHandPosition, Quaternion.Euler(initialRightHandEulerAngles));
            CurrentObject = SimulatedTrackedObject.LeftHand;
            simulationInitialized = true;
            InputStateChanged?.Invoke();
        }

        /// <summary>Feeds the same device path for keyboard control and development replay.</summary>
        public void PublishDeviceState()
        {
            if (!IsSimulationActive)
                return;
            var head = poses[0];
            var eyeOffset = head.rotation * new Vector3(0.032f, 0f, 0f);
            var headState = new XRSimulatedHMDState
            {
                isTracked = true,
                trackingState = (int)(InputTrackingState.Position | InputTrackingState.Rotation),
                devicePosition = head.position,
                deviceRotation = head.rotation,
                centerEyePosition = head.position,
                centerEyeRotation = head.rotation,
                leftEyePosition = head.position - eyeOffset,
                leftEyeRotation = head.rotation,
                rightEyePosition = head.position + eyeOffset,
                rightEyeRotation = head.rotation,
            };
            InputState.Change(simulatedHead, headState);
            InputState.Change(simulatedLeftHand, BuildControllerState(SimulatedTrackedObject.LeftHand));
            InputState.Change(simulatedRightHand, BuildControllerState(SimulatedTrackedObject.RightHand));
        }

        XRSimulatedControllerState BuildControllerState(SimulatedTrackedObject hand)
        {
            int index = (int)hand;
            var state = new XRSimulatedControllerState
            {
                isTracked = true,
                trackingState = (int)(InputTrackingState.Position | InputTrackingState.Rotation),
                devicePosition = poses[index].position,
                deviceRotation = poses[index].rotation,
                grip = grips[index] ? 1f : 0f,
                trigger = triggers[index] ? 1f : 0f,
                batteryLevel = 1f,
                primary2DAxis = InputPaused || IsSimulationResetPending ? Vector2.zero :
                    (hand == SimulatedTrackedObject.LeftHand ? walkingInput : turningInput),
            };
            state.WithButton(ControllerButton.GripButton, grips[index]);
            state.WithButton(ControllerButton.TriggerButton, triggers[index]);
            state.WithButton(ControllerButton.UserPresence, true);
            return state;
        }

        public static bool HasCommandLineArgument(string argument)
        {
            foreach (var candidate in Environment.GetCommandLineArgs())
                if (string.Equals(candidate, argument, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        static void ValidateObject(SimulatedTrackedObject trackedObject)
        {
            if (trackedObject < SimulatedTrackedObject.Head || trackedObject > SimulatedTrackedObject.RightHand)
                throw new ArgumentOutOfRangeException(nameof(trackedObject));
        }

        void OnDisable()
        {
            StopAllCoroutines();
            ShutdownInput();
        }

        void OnDestroy() => ShutdownInput();

        public void ShutdownInput()
        {
            RemoveDevice(simulatedHead);
            RemoveDevice(simulatedLeftHand);
            RemoveDevice(simulatedRightHand);
            simulatedHead = null;
            simulatedLeftHand = null;
            simulatedRightHand = null;
            IsSimulationActive = false;
            resetRequestedFrame = -1;
            StopOwnedDeviceMode();
        }

        static void RemoveDevice(UnityEngine.InputSystem.InputDevice device)
        {
            if (device != null && device.added)
                InputSystem.RemoveDevice(device);
        }

        void StopOwnedDeviceMode()
        {
            IsDeviceModeActive = false;
            startingDisplaySubsystem = null;
            if (ownedDeviceManager == null)
                return;
            // Failed loader initialization does not mark the manager initialized.
            // DeinitializeLoader already stops a successfully started subsystem.
            if (ownedDeviceManager.isInitializationComplete)
                ownedDeviceManager.DeinitializeLoader();
            ownedDeviceManager = null;
        }
    }
}
