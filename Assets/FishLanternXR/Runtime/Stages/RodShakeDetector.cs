using UnityEngine;

namespace FishLantern.XR
{
    /// <summary>Detects deliberate repeated movement while the display rod is held.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GrabInteractableEx), typeof(InteractableIdentity), typeof(InteractionGate))]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class RodShakeDetector : MonoBehaviour, IInteractionResettable
    {
        [SerializeField, Min(0.1f)] private float linearSpeedThreshold = 0.8f;
        [SerializeField, Min(0.1f)] private float angularSpeedThreshold = 2.5f;
        [SerializeField, Min(1)] private int requiredPeaks = 4;
        [SerializeField, Min(0.05f)] private float peakCooldownSeconds = 0.18f;
        [SerializeField, Min(0.01f)] private float minimumStrokeDistance = 0.045f;
        [SerializeField, Min(1f)] private float minimumStrokeAngleDegrees = 7f;
        [SerializeField, Min(0.002f)] private float reversalDistance = 0.012f;
        [SerializeField, Min(0.5f)] private float reversalAngleDegrees = 2.5f;
        [SerializeField] private EventPrerequisite prerequisite;
        [SerializeField] private InteractionEventUnityEvent onShakeProgress = new();
        [SerializeField] private InteractionEventUnityEvent onShakeComplete = new();

        private GrabInteractableEx grab;
        private InteractableIdentity identity;
        private InteractionGate gate;
        private int peakCount;
        private float peakCooldown;
        private bool completed;
        private bool hasPreviousPose;
        private Vector3 previousPosition;
        private Quaternion previousRotation;
        private float grabSettlingSeconds;
        private bool hasStrokeAxis;
        private bool angularStroke;
        private Vector3 strokeAxis;
        private float strokeCoordinate;
        private float strokeOrigin;
        private float furthestCoordinate;
        private float strokeDirection = 1f;
        private bool strokeReachedSpeed;
        private float idleSeconds;

        /// <summary>Current normalized shake progress.</summary>
        public float Progress => completed ? 1f : peakCount / (float)Mathf.Max(1, requiredPeaks);

        /// <summary>Whether the required deliberate swings have completed in this run.</summary>
        public bool IsComplete => completed;

        /// <summary>Number of confirmed direction changes after sufficient travel.</summary>
        public int CompletedSwingCount => peakCount;

        /// <summary>Number of confirmed direction changes required by this interaction.</summary>
        public int RequiredSwingCount => Mathf.Max(1, requiredPeaks);

        /// <summary>Configures motion thresholds, required peaks and optional prerequisite.</summary>
        public void Configure(
            float linearThreshold,
            float angularThreshold,
            int peaks,
            float cooldown,
            EventPrerequisite requiredEvent = null)
        {
            linearSpeedThreshold = Mathf.Max(0.1f, linearThreshold);
            angularSpeedThreshold = Mathf.Max(0.1f, angularThreshold);
            requiredPeaks = Mathf.Max(1, peaks);
            peakCooldownSeconds = Mathf.Max(0.05f, cooldown);
            prerequisite = requiredEvent;
        }

        /// <summary>Configures travel and reversal tolerances without changing the event contract.</summary>
        public void ConfigureStroke(
            float minimumTravelMetres,
            float minimumAngleDegrees,
            float reversalTravelMetres,
            float reversalDegrees)
        {
            minimumStrokeDistance = Mathf.Max(0.01f, minimumTravelMetres);
            minimumStrokeAngleDegrees = Mathf.Max(1f, minimumAngleDegrees);
            reversalDistance = Mathf.Clamp(reversalTravelMetres, 0.002f, minimumStrokeDistance);
            reversalAngleDegrees = Mathf.Clamp(reversalDegrees, 0.5f, minimumStrokeAngleDegrees);
            ClearMotionContinuity();
        }

        /// <summary>Samples held rod motion and returns normalized progress.</summary>
        public float SampleMotion(Vector3 linearVelocity, Vector3 angularVelocity, float deltaSeconds)
        {
            EnsureReferences();
            if (!CanSampleMotion() || deltaSeconds <= 0f || deltaSeconds > 0.2f)
            {
                ClearMotionContinuity();
                return Progress;
            }

            peakCooldown = Mathf.Max(0f, peakCooldown - deltaSeconds);
            if (linearVelocity.magnitude < linearSpeedThreshold * 0.1f &&
                angularVelocity.magnitude < angularSpeedThreshold * 0.1f)
            {
                idleSeconds += deltaSeconds;
                if (idleSeconds >= 0.75f)
                {
                    // A player can hold the work still, then begin a new swing on another axis.
                    ClearMotionContinuity();
                    return Progress;
                }
            }
            else idleSeconds = 0f;

            if (!hasStrokeAxis)
            {
                var linearStrength = linearVelocity.magnitude / linearSpeedThreshold;
                var angularStrength = angularVelocity.magnitude / angularSpeedThreshold;
                if (linearStrength < 1f && angularStrength < 1f) return Progress;
                angularStroke = angularStrength > linearStrength;
                strokeAxis = (angularStroke ? angularVelocity : linearVelocity).normalized;
                hasStrokeAxis = true;
            }

            // Integrate signed travel along the initial motion axis. Time above a speed threshold
            // cannot complete a stroke: the rod must travel far enough and visibly turn back.
            var signedSpeed = Vector3.Dot(angularStroke ? angularVelocity : linearVelocity, strokeAxis);
            var speedThreshold = angularStroke ? angularSpeedThreshold : linearSpeedThreshold;
            var requiredTravel = angularStroke ? minimumStrokeAngleDegrees * Mathf.Deg2Rad : minimumStrokeDistance;
            var requiredReversal = angularStroke ? reversalAngleDegrees * Mathf.Deg2Rad : reversalDistance;
            strokeCoordinate += signedSpeed * deltaSeconds;
            if (signedSpeed * strokeDirection >= speedThreshold) strokeReachedSpeed = true;
            if ((strokeCoordinate - furthestCoordinate) * strokeDirection > 0f)
                furthestCoordinate = strokeCoordinate;

            var travel = (furthestCoordinate - strokeOrigin) * strokeDirection;
            var returnTravel = (furthestCoordinate - strokeCoordinate) * strokeDirection;
            if (returnTravel < requiredReversal) return Progress;

            var deliberateSwing = travel >= requiredTravel && strokeReachedSpeed && peakCooldown <= 0f;
            strokeOrigin = furthestCoordinate;
            furthestCoordinate = strokeCoordinate;
            strokeDirection = -strokeDirection;
            strokeReachedSpeed = signedSpeed * strokeDirection >= speedThreshold;
            if (!deliberateSwing) return Progress;

            peakCooldown = peakCooldownSeconds;
            peakCount++;
            var progressEvent = identity.CreateEvent(
                InteractionKind.RodShake,
                InteractionResult.Progressed,
                progress: Progress);
            InteractionServiceHub.Active?.Report(progressEvent);
            onShakeProgress.Invoke(progressEvent);

            if (peakCount >= requiredPeaks)
                Complete();
            return Progress;
        }

        /// <summary>Clears counted peaks and completion state.</summary>
        public void ResetInteraction()
        {
            peakCount = 0;
            completed = false;
            ClearMotionContinuity();
        }

        /// <summary>Breaks motion across release, pause and recovery while preserving confirmed swings.</summary>
        public void ClearMotionContinuity()
        {
            peakCooldown = 0f;
            hasPreviousPose = false;
            grabSettlingSeconds = 0f;
            hasStrokeAxis = false;
            angularStroke = false;
            strokeAxis = Vector3.zero;
            strokeCoordinate = 0f;
            strokeOrigin = 0f;
            furthestCoordinate = 0f;
            strokeDirection = 1f;
            strokeReachedSpeed = false;
            idleSeconds = 0f;
        }

        private void Awake()
        {
            EnsureReferences();
        }

        private void LateUpdate()
        {
            EnsureReferences();
            if (!CanSampleMotion())
            {
                ClearMotionContinuity();
                return;
            }
            if (!hasPreviousPose)
            {
                previousPosition = transform.position;
                previousRotation = transform.rotation;
                grabSettlingSeconds = 0.2f;
                hasPreviousPose = true;
                return;
            }

            var seconds = Time.deltaTime;
            if (seconds <= 0f) return;
            // Recovery, tracking reacquisition and a stalled frame are not player swings.
            if (seconds > 0.2f || Vector3.Distance(transform.position, previousPosition) > 0.15f ||
                Quaternion.Angle(transform.rotation, previousRotation) > 30f)
            {
                ClearMotionContinuity();
                return;
            }
            var linearVelocity = (transform.position - previousPosition) / seconds;
            var rotationDelta = transform.rotation * Quaternion.Inverse(previousRotation);
            rotationDelta.ToAngleAxis(out var angle, out var axis);
            if (angle > 180f) angle -= 360f;
            var angularVelocity = Mathf.Abs(angle) > 0.001f ? axis * (angle * Mathf.Deg2Rad / seconds) : Vector3.zero;
            previousPosition = transform.position;
            previousRotation = transform.rotation;
            // Instantaneous XR grabs move Transforms, so Rigidbody velocity is not a reliable motion source.
            // Ignore attach easing so picking up the rod does not itself count as a display gesture.
            if (grabSettlingSeconds > 0f)
            {
                grabSettlingSeconds -= seconds;
                return;
            }
            SampleMotion(linearVelocity, angularVelocity, seconds);
        }

        private void OnDisable() => ClearMotionContinuity();

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) ClearMotionContinuity();
        }

        private bool CanSampleMotion()
        {
            return !completed && isActiveAndEnabled && Application.isFocused && grab != null &&
                   grab.IsHeldByHand && (prerequisite == null || prerequisite.IsSatisfied) &&
                   gate.CanPerform(InteractionKind.RodShake, reportDenial: false);
        }

        private void Complete()
        {
            completed = true;
            var eventData = identity.CreateEvent(
                InteractionKind.RodShake,
                InteractionResult.Succeeded,
                progress: 1f);
            var hub = InteractionServiceHub.Active;
            hub?.Report(eventData);
            hub?.FeedbackService?.PlaySuccess(identity, InteractionKind.RodShake);
            onShakeComplete.Invoke(eventData);
        }

        private void EnsureReferences()
        {
            grab ??= GetComponent<GrabInteractableEx>();
            identity ??= GetComponent<InteractableIdentity>();
            gate ??= GetComponent<InteractionGate>();
        }
    }
}
