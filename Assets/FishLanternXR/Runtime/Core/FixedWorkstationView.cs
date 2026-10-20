using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FishLantern.XR
{
    /// <summary>
    /// Keeps the player at the workbench while allowing a small, comfortable horizontal turn.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(XROrigin))]
    public sealed class FixedWorkstationView : MonoBehaviour, IInteractionResettable
    {
        [SerializeField, Range(5f, 60f)] private float maximumYaw = 35f;
        [SerializeField, Min(10f)] private float yawSpeed = 55f;

        private XROrigin origin;
        private InputAction yawAction;
        private Vector3 initialOriginPosition;
        private Quaternion initialOriginRotation;
        private Vector3 fixedCameraPosition;
        private float currentYaw;
        private bool cameraPositionCaptured;

        /// <summary>Maximum horizontal turn to either side of the initial workstation view.</summary>
        public float MaximumYaw => maximumYaw;

        /// <summary>Whether the camera anchor has been captured and is actively position-locked.</summary>
        public bool IsPositionLocked => cameraPositionCaptured;

        /// <summary>Configures the workstation-only turn range and speed.</summary>
        public void Configure(float yawLimitDegrees, float degreesPerSecond)
        {
            maximumYaw = Mathf.Clamp(yawLimitDegrees, 5f, 60f);
            yawSpeed = Mathf.Max(10f, degreesPerSecond);
        }

        /// <inheritdoc />
        public void ResetInteraction()
        {
            currentYaw = 0f;
            transform.SetPositionAndRotation(initialOriginPosition, initialOriginRotation);
            if (origin != null && origin.Camera != null)
                fixedCameraPosition = origin.Camera.transform.position;
            cameraPositionCaptured = origin != null && origin.Camera != null;
        }

        private void Awake()
        {
            origin = GetComponent<XROrigin>();
            initialOriginPosition = transform.position;
            initialOriginRotation = transform.rotation;

            yawAction = new InputAction("Workstation Yaw", InputActionType.Value, expectedControlType: "Axis");
            yawAction.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/leftArrow")
                .With("Positive", "<Keyboard>/rightArrow");
            yawAction.AddBinding("<XRController>{RightHand}/primary2DAxis/x");
        }

        private void OnEnable()
        {
            yawAction?.Enable();
            cameraPositionCaptured = false;
        }

        private void OnDisable()
        {
            yawAction?.Disable();
        }

        private void OnDestroy()
        {
            yawAction?.Dispose();
        }

        private void Update()
        {
            if (yawAction == null)
                return;

            var input = Mathf.Clamp(yawAction.ReadValue<float>(), -1f, 1f);
            currentYaw = Mathf.Clamp(
                currentYaw + input * yawSpeed * Time.unscaledDeltaTime,
                -maximumYaw,
                maximumYaw);
        }

        private void LateUpdate()
        {
            if (origin == null || origin.Camera == null)
                return;

            if (!cameraPositionCaptured)
            {
                fixedCameraPosition = origin.Camera.transform.position;
                cameraPositionCaptured = true;
            }

            transform.rotation = Quaternion.AngleAxis(currentYaw, Vector3.up) * initialOriginRotation;
            transform.position += fixedCameraPosition - origin.Camera.transform.position;
        }

        private void OnValidate()
        {
            maximumYaw = Mathf.Clamp(maximumYaw, 5f, 60f);
            yawSpeed = Mathf.Max(10f, yawSpeed);
        }
    }
}
