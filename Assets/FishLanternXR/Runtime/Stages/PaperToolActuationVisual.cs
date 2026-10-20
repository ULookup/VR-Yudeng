using UnityEngine;

namespace FishLantern.XR
{
    /// <summary>Displays scissors actuation from the existing held-tool input without publishing progress.</summary>
    [DisallowMultipleComponent]
    public sealed class PaperToolActuationVisual : MonoBehaviour, IInteractionResettable
    {
        [SerializeField] private GrabInteractableEx tool;
        [SerializeField] private Transform leftBlade;
        [SerializeField] private Transform rightBlade;
        [SerializeField, Range(0f, 45f)] private float openAngle = 12f;
        [SerializeField, Min(0.01f)] private float actuationSeconds = 0.09f;
        [SerializeField] private Vector3 localHingeAxis = Vector3.up;
        [SerializeField] private Quaternion leftClosedRotation = Quaternion.identity;
        [SerializeField] private Quaternion rightClosedRotation = Quaternion.identity;
        [SerializeField] private bool restRotationsCaptured;

        private float opening = 1f;

        /// <summary>Both blade roots must use the scissor hinge as their origin in the closed pose.</summary>
        public void Configure(GrabInteractableEx heldTool, Transform firstBlade, Transform secondBlade,
            float openingAngle = 12f)
        {
            // Re-running the scene upgrade must not treat the displayed open pose as a new rest pose.
            if (restRotationsCaptured)
            {
                if (leftBlade != null) leftBlade.localRotation = leftClosedRotation;
                if (rightBlade != null) rightBlade.localRotation = rightClosedRotation;
            }
            tool = heldTool;
            leftBlade = firstBlade;
            rightBlade = secondBlade;
            openAngle = Mathf.Clamp(openingAngle, 0f, 45f);
            restRotationsCaptured = false;
            CaptureRestRotations();
            ResetInteraction();
        }

        /// <summary>Uses the model's local hinge normal; this does not change input or contact geometry.</summary>
        public void ConfigureHingeAxis(Vector3 axis)
        {
            localHingeAxis = axis.sqrMagnitude > 0.0001f ? axis.normalized : Vector3.up;
            ApplyPose();
        }

        public void ResetInteraction()
        {
            CaptureRestRotations();
            opening = 1f;
            ApplyPose();
        }

        private void Awake() => CaptureRestRotations();

        private void LateUpdate()
        {
            var target = tool != null && tool.TriggerPressed ? 0f : 1f;
            opening = Mathf.MoveTowards(opening, target, Time.deltaTime / Mathf.Max(0.01f, actuationSeconds));
            ApplyPose();
        }

        private void CaptureRestRotations()
        {
            if (restRotationsCaptured) return;
            if (leftBlade != null) leftClosedRotation = leftBlade.localRotation;
            if (rightBlade != null) rightClosedRotation = rightBlade.localRotation;
            restRotationsCaptured = true;
        }

        private void ApplyPose()
        {
            var axis = localHingeAxis.sqrMagnitude > 0.0001f ? localHingeAxis.normalized : Vector3.up;
            if (leftBlade != null)
                leftBlade.localRotation = leftClosedRotation * Quaternion.AngleAxis(openAngle * opening, axis);
            if (rightBlade != null)
                rightBlade.localRotation = rightClosedRotation * Quaternion.AngleAxis(-openAngle * opening, axis);
        }
    }
}
