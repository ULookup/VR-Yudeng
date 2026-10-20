using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace FishLantern.XR
{
    /// <summary>Detects a sustained two-hand compression gesture and reports bamboo bending progress.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GrabInteractableEx), typeof(InteractableIdentity), typeof(InteractionGate))]
    public sealed class TwoHandBendInteraction : MonoBehaviour, IInteractionResettable
    {
        [SerializeField, Min(0.01f)] private float requiredDistanceReduction = 0.08f;
        [SerializeField, Min(0.01f)] private float holdSeconds = 0.45f;
        [SerializeField, Min(0.01f)] private float minimumStartingDistance = 0.12f;
        // Retain the old serialized fields so existing prefabs keep their references during scene migration.
        [SerializeField, HideInInspector] private float visualBendAngle = 35f;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private BambooStripDeformation deformation;
        [SerializeField] private InteractionEventUnityEvent onBendProgress = new();
        [SerializeField] private InteractionEventUnityEvent onBendComplete = new();

        private GrabInteractableEx grab;
        private InteractableIdentity identity;
        private InteractionGate gate;
        private Vector3 initialLocalScale;
        private float startingDistance;
        private float startingVisualProgress;
        private float holdElapsed;
        private float lastReportedProgress;
        private bool gestureActive;
        private bool completed;
        private bool scaleCaptured;

        /// <summary>Raised as normalized bend progress changes.</summary>
        public InteractionEventUnityEvent OnBendProgress => onBendProgress;

        /// <summary>Raised once after the bend threshold is sustained.</summary>
        public InteractionEventUnityEvent OnBendComplete => onBendComplete;

        public bool IsComplete => completed;
        public float VisualProgress => deformation != null ? deformation.Progress : 0f;

        /// <summary>Configures the existing gesture thresholds; mesh deformation is bound separately.</summary>
        public void Configure(
            float distanceReduction,
            float requiredHoldSeconds,
            Transform bendVisual = null)
        {
            requiredDistanceReduction = Mathf.Max(0.01f, distanceReduction);
            holdSeconds = Mathf.Max(0.01f, requiredHoldSeconds);
            visualRoot = bendVisual;
        }

        /// <summary>Binds the length-preserving presentation without changing the authoritative event path.</summary>
        public void ConfigureDeformation(BambooStripDeformation bambooDeformation)
        {
            deformation = bambooDeformation;
            EnsureReferences();
            grab.trackScale = false;
        }

        /// <summary>Begins evaluating a two-hand bend from the supplied hand distance.</summary>
        public bool BeginBend(float handDistance)
        {
            EnsureReferences();
            if (completed || !IsFinite(handDistance) || handDistance < minimumStartingDistance ||
                !TryGetTwoHandDistance(out _) || deformation == null ||
                !gate.CanPerform(InteractionKind.BendProgress, InteractionHand.Both))
            {
                return false;
            }

            startingDistance = handDistance;
            startingVisualProgress = deformation.Progress;
            holdElapsed = 0f;
            gestureActive = true;
            return true;
        }

        /// <summary>Evaluates a controller sample only while the real XRI selection and permission remain valid.</summary>
        public float EvaluateHandDistance(float handDistance, float deltaSeconds)
        {
            if (!gestureActive || completed)
                return completed ? 1f : 0f;
            if (!IsFinite(handDistance) || !IsFinite(deltaSeconds) ||
                !TryGetTwoHandDistance(out _) ||
                !gate.CanPerform(InteractionKind.BendProgress, InteractionHand.Both, reportDenial: false))
            {
                CancelGesture();
                return 0f;
            }

            var compression = Mathf.Max(0f, startingDistance - handDistance);
            var geometricProgress = Mathf.Clamp01(compression / requiredDistanceReduction);
            if (geometricProgress >= 1f)
                holdElapsed += Mathf.Max(0f, deltaSeconds);
            else
                holdElapsed = Mathf.Max(0f, holdElapsed - Mathf.Max(0f, deltaSeconds) * 0.5f);

            var holdProgress = Mathf.Clamp01(holdElapsed / holdSeconds);
            var progress = Mathf.Min(geometricProgress, holdProgress);
            var remainingBend = Mathf.Max(geometricProgress * 0.85f, progress);
            if (!ApplyVisual(Mathf.Lerp(startingVisualProgress, 1f, remainingBend)))
            {
                CancelGesture();
                return 0f;
            }
            ReportProgress(progress);

            if (progress >= 1f)
                Complete();
            return progress;
        }

        /// <summary>Restores the straight mesh, anchors, colliders and original material scale.</summary>
        public void ResetInteraction()
        {
            EnsureReferences();
            CaptureScale();
            gestureActive = false;
            completed = false;
            startingDistance = 0f;
            startingVisualProgress = 0f;
            holdElapsed = 0f;
            lastReportedProgress = 0f;
            transform.localScale = initialLocalScale;
            deformation?.ResetInteraction();
        }

        private void Awake()
        {
            EnsureReferences();
            CaptureScale();
            // XRI's normal two-hand scaling must not stretch the material while compression bends it.
            grab.trackScale = false;
        }

        private void CaptureScale()
        {
            if (scaleCaptured) return;
            initialLocalScale = transform.localScale;
            scaleCaptured = true;
        }

        private void Update()
        {
            if (completed) return;
            if (!TryGetTwoHandDistance(out var distance))
            {
                CancelGesture();
                return;
            }

            if (!gestureActive && !BeginBend(distance))
                return;

            EvaluateHandDistance(distance, Time.deltaTime);
        }

        private void ReportProgress(float progress)
        {
            if (progress < 1f && Mathf.Abs(progress - lastReportedProgress) < 0.1f)
                return;

            lastReportedProgress = progress;
            var eventData = identity.CreateEvent(
                InteractionKind.BendProgress,
                InteractionResult.Progressed,
                progress: progress,
                hand: InteractionHand.Both);
            InteractionServiceHub.Active?.Report(eventData);
            onBendProgress.Invoke(eventData);
        }

        private void Complete()
        {
            completed = true;
            gestureActive = false;
            ApplyVisual(1f);
            var eventData = identity.CreateEvent(
                InteractionKind.BendComplete,
                InteractionResult.Succeeded,
                progress: 1f,
                hand: InteractionHand.Both);
            var hub = InteractionServiceHub.Active;
            hub?.Report(eventData);
            hub?.FeedbackService?.PlaySuccess(identity, InteractionKind.BendComplete);
            onBendComplete.Invoke(eventData);
        }

        private bool ApplyVisual(float progress)
        {
            return deformation != null && deformation.SetProgress(progress);
        }

        private void CancelGesture()
        {
            var wasActive = gestureActive;
            gestureActive = false;
            holdElapsed = 0f;
            // Releasing or pausing leaves the current shape in place. A full reset is the only straightening action.
            if (wasActive && lastReportedProgress > 0f)
                ReportProgress(0f);
        }

        private bool TryGetTwoHandDistance(out float distance)
        {
            Transform left = null;
            Transform right = null;
            foreach (var selecting in grab.interactorsSelecting)
            {
                if (selecting is XRSocketInteractor) continue;
                switch (InteractionHandResolver.Resolve(selecting))
                {
                    case InteractionHand.Left:
                        left = selecting.transform;
                        break;
                    case InteractionHand.Right:
                        right = selecting.transform;
                        break;
                }
            }
            distance = left != null && right != null ? Vector3.Distance(left.position, right.position) : 0f;
            return left != null && right != null;
        }

        private void OnDisable()
        {
            CancelGesture();
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void EnsureReferences()
        {
            grab ??= GetComponent<GrabInteractableEx>();
            identity ??= GetComponent<InteractableIdentity>();
            gate ??= GetComponent<InteractionGate>();
            deformation ??= GetComponent<BambooStripDeformation>();
        }
    }
}
