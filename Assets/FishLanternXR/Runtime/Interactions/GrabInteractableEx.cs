using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace FishLantern.XR
{
    /// <summary>Allowed controller hands for a reusable grab interactable.</summary>
    public enum GrabHandRequirement
    {
        Either = 0,
        LeftOnly,
        RightOnly,
        BothHandsSupported,
    }

    /// <summary>Physical behavior applied after the last hand releases an object.</summary>
    public enum GrabReleaseMode
    {
        Drop = 0,
        HoldInPlace,
        ReturnToStart,
        SocketPreferred,
        WorktableHoldOrReturn,
    }

    /// <summary>
    /// Project grab primitive with step gating, hand/ray restrictions, normalized events and reset behavior.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(InteractableIdentity), typeof(InteractionGate), typeof(Rigidbody))]
    public sealed class GrabInteractableEx : XRGrabInteractable, IInteractionResettable
    {
        [Header("Fish Lantern Grab Rules")]
        [SerializeField] private GrabHandRequirement handRequirement = GrabHandRequirement.Either;
        [SerializeField] private bool allowRemoteGrab;
        [SerializeField] private GrabReleaseMode releaseMode = GrabReleaseMode.Drop;
        [SerializeField, Min(0.01f)] private float returnDuration = 0.55f;

        [Header("Project Events")]
        [SerializeField] private InteractionEventUnityEvent onGrab = new();
        [SerializeField] private InteractionEventUnityEvent onRelease = new();
        [SerializeField] private InteractionEventUnityEvent onSocketed = new();

        private InteractableIdentity identity;
        private InteractionGate gate;
        private Rigidbody body;
        private Vector3 initialPosition;
        private Quaternion initialRotation;
        private Transform initialParent;
        private bool initialPoseCaptured;
        private bool triggerPressed;
        private Coroutine returnRoutine;
        private WorktableSurfaceBoundary worktableBoundary;
        private WorktablePlacementZone worktablePlacementZone;
        private bool interactionSuspended;
        private bool fixedInPlace;
        private bool hasBeenSocketed;
        private Vector3 suspendedPosition;
        private Quaternion suspendedRotation;
        private bool resumeKinematic;
        private bool resumeGravity;
        private bool resumeReturn;

        /// <summary>Raised after a permitted hand or ray grab begins.</summary>
        public InteractionEventUnityEvent OnGrab => onGrab;

        /// <summary>Raised whenever an interactor releases this object.</summary>
        public InteractionEventUnityEvent OnRelease => onRelease;

        /// <summary>Raised when an XR Socket Interactor selects this object.</summary>
        public InteractionEventUnityEvent OnSocketed => onSocketed;

        /// <summary>Whether an XR Ray Interactor may remotely select this object.</summary>
        public bool AllowRemoteGrab
        {
            get => allowRemoteGrab;
            set => allowRemoteGrab = value;
        }

        /// <summary>Configured hand restriction.</summary>
        public GrabHandRequirement HandRequirement => handRequirement;

        /// <summary>Configured behavior after the last hand releases the object.</summary>
        public GrabReleaseMode ReleaseMode => releaseMode;

        /// <summary>Duration of the smooth return motion after an ordinary release.</summary>
        public float ReturnDuration => returnDuration;

        /// <summary>True while an actual hand or ray holds this object; a socket is not a hand.</summary>
        public bool IsHeldByHand
        {
            get
            {
                foreach (var interactor in interactorsSelecting)
                    if (interactor is not XRSocketInteractor) return true;
                return false;
            }
        }

        /// <summary>Activation supplied by the same XRI input path used on the controllers.</summary>
        public bool TriggerPressed => triggerPressed && IsHeldByHand && !interactionSuspended;

        /// <summary>Installed and fixed pieces are excluded from non-destructive tool recovery.</summary>
        public bool IsRecoveryProtected => fixedInPlace || hasBeenSocketed;

        /// <summary>Returns whether the existing selection belongs to the supplied socket.</summary>
        public bool IsSelectedBySocket(string socketId)
        {
            foreach (var interactor in interactorsSelecting)
                if (interactor is XRSocketInteractor && ResolveSocketId(interactor) == socketId) return true;
            return false;
        }

        /// <summary>Releases hands without cancelling an installed socket connection.</summary>
        public void ReleaseHandSelections()
        {
            triggerPressed = false;
            if (interactionManager == null) return;
            for (var index = interactorsSelecting.Count - 1; index >= 0; index--)
            {
                var interactor = interactorsSelecting[index];
                if (interactor is not XRSocketInteractor)
                    interactionManager.SelectCancel(interactor, this);
            }
        }

        /// <summary>Freezes only this production object while tracking and UI input remain active.</summary>
        public void SetInteractionSuspended(bool suspended)
        {
            if (interactionSuspended == suspended) return;
            EnsureReferences();
            interactionSuspended = suspended;
            triggerPressed = false;
            if (suspended)
            {
                resumeReturn = returnRoutine != null;
                StopReturnMotion();
                suspendedPosition = transform.position;
                suspendedRotation = transform.rotation;
                ReleaseHandSelections();
                // Cancellation restores XRI's pre-grab Rigidbody settings synchronously.
                resumeKinematic = body.isKinematic;
                resumeGravity = body.useGravity;
                ApplySuspendedPose();
            }
            else if (!isSelected)
            {
                body.isKinematic = resumeKinematic || fixedInPlace;
                body.useGravity = resumeGravity && !fixedInPlace;
                if (resumeReturn && !IsRecoveryProtected && isActiveAndEnabled)
                    returnRoutine = StartCoroutine(ReturnToInitialPose());
                resumeReturn = false;
            }
            else resumeReturn = false;
        }

        /// <summary>Protects a completed assembly from grabs and tool recovery until a full restart.</summary>
        public void MarkFixedInPlace()
        {
            EnsureReferences();
            fixedInPlace = true;
            StopReturnMotion();
            ReleaseHandSelections();
            ClearMotion();
            body.isKinematic = true;
            body.useGravity = false;
        }

        /// <summary>Returns an uninstalled, released object without clearing its crafting components.</summary>
        public bool TryRecoverInitialPose()
        {
            if (IsRecoveryProtected || isSelected) return false;
            EnsureReferences();
            CaptureInitialPose();
            StopReturnMotion();
            triggerPressed = false;
            RestoreInitialPose();
            resumeReturn = false;
            if (interactionSuspended)
            {
                suspendedPosition = initialPosition;
                suspendedRotation = initialRotation;
                resumeKinematic = body.isKinematic;
                resumeGravity = body.useGravity;
                ApplySuspendedPose();
            }
            return true;
        }

        /// <summary>Captures inactive-stage objects before a full experience reset is requested.</summary>
        public void CaptureInitialPose()
        {
            if (initialPoseCaptured) return;
            initialPosition = transform.position;
            initialRotation = transform.rotation;
            initialParent = transform.parent;
            initialPoseCaptured = true;
        }

        /// <summary>Configures common grab behavior from editor bootstrap code.</summary>
        public void Configure(
            GrabHandRequirement requiredHand,
            bool remoteGrab,
            GrabReleaseMode afterRelease,
            Transform primaryAttach = null,
            Transform secondaryAttach = null,
            float smoothReturnSeconds = 0.55f)
        {
            handRequirement = requiredHand;
            allowRemoteGrab = remoteGrab;
            releaseMode = afterRelease;
            returnDuration = Mathf.Max(0.01f, smoothReturnSeconds);
            attachTransform = primaryAttach;
            secondaryAttachTransform = secondaryAttach;
            ApplySelectMode();
            ApplyRestingPhysics();
        }

        /// <inheritdoc />
        public override bool IsSelectableBy(IXRSelectInteractor interactor)
        {
            if (!base.IsSelectableBy(interactor))
                return false;

            // Keep an existing socket alive during pause, but never admit a new installation.
            if (interactionSuspended || IsRecoveryProtected)
                return interactor is XRSocketInteractor && interactorsSelecting.Contains(interactor);

            EnsureReferences();
            var hand = InteractionHandResolver.Resolve(interactor);
            if (!IsHandAllowed(hand))
            {
                gate.Deny(InteractionKind.Grab, InteractionFailureReason.WrongHand, hand);
                return false;
            }

            if (interactor is XRRayInteractor && !allowRemoteGrab)
            {
                gate.Deny(InteractionKind.Grab, InteractionFailureReason.RemoteGrabDisabled, hand);
                return false;
            }

            var kind = interactor is XRSocketInteractor ? InteractionKind.Socketed : InteractionKind.Grab;
            var targetId = ResolveSocketId(interactor);
            return gate.CanPerform(kind, hand, targetId);
        }

        /// <inheritdoc />
        public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase updatePhase)
        {
            base.ProcessInteractable(updatePhase);

            if (interactionSuspended)
            {
                ApplySuspendedPose();
                return;
            }

            if (!isSelected ||
                (updatePhase != XRInteractionUpdateOrder.UpdatePhase.Dynamic &&
                 updatePhase != XRInteractionUpdateOrder.UpdatePhase.OnBeforeRender &&
                 updatePhase != XRInteractionUpdateOrder.UpdatePhase.Fixed))
            {
                return;
            }

            XRRayInteractor selectingRay = null;
            foreach (var interactor in interactorsSelecting)
            {
                if (interactor is XRSocketInteractor)
                    return;
                if (interactor is XRRayInteractor ray)
                    selectingRay = ray;
            }

            if (selectingRay == null)
                return;

            worktableBoundary ??= FindFirstObjectByType<WorktableSurfaceBoundary>();
            worktableBoundary?.ConstrainRemoteGrab(transform, selectingRay);
        }

        /// <summary>Restores the initial pose and physical state captured on awake.</summary>
        public void ResetInteraction()
        {
            EnsureReferences();
            CaptureInitialPose();
            if (interactionManager != null && isSelected)
                interactionManager.CancelInteractableSelection((IXRSelectInteractable)this);
            triggerPressed = false;
            interactionSuspended = false;
            fixedInPlace = false;
            hasBeenSocketed = false;
            resumeReturn = false;
            StopReturnMotion();
            RestoreInitialPose();
        }

        /// <inheritdoc />
        protected override void Awake()
        {
            base.Awake();
            EnsureReferences();
            CaptureInitialPose();
            ApplySelectMode();
            ApplyRestingPhysics();
        }

        /// <inheritdoc />
        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            EnsureReferences();

            if (returnRoutine != null)
            {
                StopCoroutine(returnRoutine);
                returnRoutine = null;
            }

            // XRGrabInteractable has already selected the Rigidbody mode required by its movement type.
            var hand = InteractionHandResolver.Resolve(args.interactorObject);
            if (args.interactorObject is XRSocketInteractor)
            {
                hasBeenSocketed = true;
                var socketId = ResolveSocketId(args.interactorObject);
                var socketed = identity.CreateEvent(
                    InteractionKind.Socketed,
                    InteractionResult.Succeeded,
                    progress: 1f,
                    targetId: socketId,
                    hand: hand);
                onSocketed.Invoke(socketed);
                return;
            }

            var grabbed = identity.CreateEvent(
                InteractionKind.Grab,
                InteractionResult.Started,
                hand: hand);
            InteractionServiceHub.Active?.Report(grabbed);
            onGrab.Invoke(grabbed);
        }

        /// <inheritdoc />
        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args);
            EnsureReferences();
            if (!IsHeldByHand) triggerPressed = false;

            var hand = InteractionHandResolver.Resolve(args.interactorObject);
            var released = identity.CreateEvent(
                InteractionKind.Release,
                InteractionResult.Succeeded,
                progress: 1f,
                hand: hand);
            InteractionServiceHub.Active?.Report(released);
            onRelease.Invoke(released);

            if (interactorsSelecting.Count != 0 || args.interactorObject is XRSocketInteractor ||
                args.isCanceled || !isActiveAndEnabled || interactionSuspended || IsRecoveryProtected)
                return;

            ApplyReleaseMode();
        }

        protected override void OnActivated(ActivateEventArgs args)
        {
            base.OnActivated(args);
            triggerPressed = IsHeldByHand && !interactionSuspended;
        }

        protected override void OnDeactivated(DeactivateEventArgs args)
        {
            base.OnDeactivated(args);
            triggerPressed = false;
        }

        protected override void Detach()
        {
            // Recovery cancellation must not enqueue a throw into a body held still for pause.
            if (interactionSuspended || IsRecoveryProtected) return;
            base.Detach();
        }

        private bool IsHandAllowed(InteractionHand hand)
        {
            if (hand == InteractionHand.Unknown)
                return true;

            return handRequirement switch
            {
                GrabHandRequirement.LeftOnly => hand == InteractionHand.Left,
                GrabHandRequirement.RightOnly => hand == InteractionHand.Right,
                _ => true,
            };
        }

        private void ApplyReleaseMode()
        {
            switch (releaseMode)
            {
                case GrabReleaseMode.HoldInPlace:
                    ClearMotion();
                    body.isKinematic = true;
                    break;
                case GrabReleaseMode.ReturnToStart:
                case GrabReleaseMode.SocketPreferred:
                    returnRoutine = StartCoroutine(ReturnToInitialPose());
                    break;
                case GrabReleaseMode.WorktableHoldOrReturn:
                    worktablePlacementZone ??= FindFirstObjectByType<WorktablePlacementZone>();
                    if (worktablePlacementZone != null &&
                        worktablePlacementZone.TryGetPlacementPosition(transform, out var placementPosition))
                    {
                        ClearMotion();
                        body.isKinematic = true;
                        transform.position = placementPosition;
                    }
                    else
                    {
                        returnRoutine = StartCoroutine(ReturnToInitialPose());
                    }
                    break;
            }
        }

        private IEnumerator ReturnToInitialPose()
        {
            var startPosition = transform.position;
            var startRotation = transform.rotation;
            ClearMotion();
            body.isKinematic = true;

            var elapsed = 0f;
            while (elapsed < returnDuration && !isSelected)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / returnDuration);
                t = t * t * (3f - 2f * t);
                transform.SetPositionAndRotation(
                    Vector3.Lerp(startPosition, initialPosition, t),
                    Quaternion.Slerp(startRotation, initialRotation, t));
                yield return null;
            }

            if (!isSelected)
            {
                transform.SetParent(initialParent, true);
                transform.SetPositionAndRotation(initialPosition, initialRotation);
                ApplyRestingPhysics();
            }

            returnRoutine = null;
        }

        private void ApplySelectMode()
        {
            selectMode = handRequirement == GrabHandRequirement.BothHandsSupported
                ? InteractableSelectMode.Multiple
                : InteractableSelectMode.Single;
            // Placement and return modes own release motion; XRI must not also throw the anchored body.
            throwOnDetach = releaseMode == GrabReleaseMode.Drop;
        }

        private void ApplyRestingPhysics()
        {
            if (body == null)
                return;

            var anchored = releaseMode is GrabReleaseMode.ReturnToStart or
                GrabReleaseMode.SocketPreferred or GrabReleaseMode.HoldInPlace or
                GrabReleaseMode.WorktableHoldOrReturn;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.isKinematic = anchored;
            body.useGravity = !anchored;
        }

        private void ClearMotion()
        {
            if (body == null || body.isKinematic) return;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        private void StopReturnMotion()
        {
            if (returnRoutine == null) return;
            StopCoroutine(returnRoutine);
            returnRoutine = null;
        }

        private void RestoreInitialPose()
        {
            ClearMotion();
            transform.SetParent(initialParent, true);
            transform.SetPositionAndRotation(initialPosition, initialRotation);
            body.position = initialPosition;
            body.rotation = initialRotation;
            ApplyRestingPhysics();
        }

        private void ApplySuspendedPose()
        {
            // Sockets own installed poses. Only released objects need their physics held still.
            if (body == null || isSelected) return;
            ClearMotion();
            body.isKinematic = true;
            body.useGravity = false;
            transform.SetPositionAndRotation(suspendedPosition, suspendedRotation);
            body.position = suspendedPosition;
            body.rotation = suspendedRotation;
        }

        private string ResolveSocketId(IXRInteractor interactor)
        {
            if (interactor is not XRSocketInteractor)
                return string.Empty;

            var socketIdentity = interactor.transform.GetComponent<InteractableIdentity>();
            return socketIdentity != null ? socketIdentity.StableId : interactor.transform.name;
        }

        private void EnsureReferences()
        {
            identity ??= GetComponent<InteractableIdentity>();
            gate ??= GetComponent<InteractionGate>();
            body ??= GetComponent<Rigidbody>();
        }

        private void OnValidate()
        {
            returnDuration = Mathf.Max(0.01f, returnDuration);
            ApplySelectMode();
        }
    }
}
