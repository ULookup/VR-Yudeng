using System;
using UnityEngine;

namespace FishLantern.XR
{
    /// <summary>Authoritative fish-lantern experience steps.</summary>
    public enum FishLanternStep
    {
        None = 0,
        Welcome = 10,
        FrameMaking = 20,
        Papering = 30,
        SketchAndPaint = 40,
        LampDisplay = 50,
        Complete = 60,
    }

    /// <summary>Stable semantic category of an interactable object.</summary>
    public enum InteractableObjectType
    {
        Unknown = 0,
        BambooStrip50Cm,
        BambooRing53Cm,
        BambooRing45Cm,
        ShortBamboo16Cm,
        FinBamboo,
        WoodenRod,
        Wire,
        Tape,
        Pencil,
        Scissors,
        Glue,
        Paper,
        Brush,
        Paint,
        FishHead,
        FishBody,
        FishTail,
        FishFin,
        FinishedFrame,
        PaperedFrame,
        FinishedLantern,
        Button,
        Socket,
    }

    /// <summary>Normalized interaction kinds reported by all input paths.</summary>
    public enum InteractionKind
    {
        None = 0,
        Grab,
        Release,
        Socketed,
        Activate,
        ButtonPressed,
        TriggerEntered,
        BendProgress,
        BendComplete,
        TapeFixed,
        Marked,
        CutProgress,
        CutComplete,
        GlueProgress,
        GlueApplied,
        PaperAttached,
        SketchProgress,
        SketchComplete,
        PaintProgress,
        PaintComplete,
        LampInstalled,
        LightOn,
        RodShake,
        StepComplete,
        Start,
        Next,
        Restart,
        Exit,
    }

    /// <summary>Outcome represented by an interaction event.</summary>
    public enum InteractionResult
    {
        Started = 0,
        Progressed,
        Succeeded,
        Failed,
        Cancelled,
    }

    /// <summary>Machine-readable reason for a denied or failed interaction.</summary>
    public enum InteractionFailureReason
    {
        None = 0,
        WrongStep,
        WrongObject,
        WrongHand,
        RemoteGrabDisabled,
        TwoHandsRequired,
        PrerequisiteMissing,
        NotBent,
        Misaligned,
        SocketOccupied,
        Cooldown,
        AlreadyCompleted,
        InvalidTarget,
        Cancelled,
    }

    /// <summary>Logical hand responsible for an interaction.</summary>
    public enum InteractionHand
    {
        Unknown = 0,
        Left,
        Right,
        Both,
    }

    /// <summary>Request passed to the authoritative permission service before interaction.</summary>
    [Serializable]
    public struct InteractionRequest
    {
        [SerializeField] private string objectId;
        [SerializeField] private InteractableObjectType objectType;
        [SerializeField] private FishLanternStep requiredStep;
        [SerializeField] private InteractionKind interactionKind;
        [SerializeField] private InteractionHand hand;
        [SerializeField] private string targetId;

        /// <summary>Stable source object ID.</summary>
        public string ObjectId => objectId;

        /// <summary>Semantic source object type.</summary>
        public InteractableObjectType ObjectType => objectType;

        /// <summary>Step in which this request is valid.</summary>
        public FishLanternStep RequiredStep => requiredStep;

        /// <summary>Requested interaction operation.</summary>
        public InteractionKind InteractionKind => interactionKind;

        /// <summary>Hand attempting the operation.</summary>
        public InteractionHand Hand => hand;

        /// <summary>Optional socket or target ID.</summary>
        public string TargetId => targetId;

        /// <summary>Creates a permission request.</summary>
        public InteractionRequest(
            string objectId,
            InteractableObjectType objectType,
            FishLanternStep requiredStep,
            InteractionKind interactionKind,
            InteractionHand hand = InteractionHand.Unknown,
            string targetId = "")
        {
            this.objectId = objectId ?? string.Empty;
            this.objectType = objectType;
            this.requiredStep = requiredStep;
            this.interactionKind = interactionKind;
            this.hand = hand;
            this.targetId = targetId ?? string.Empty;
        }
    }

    /// <summary>Serializable payload sent from the interaction layer to the state layer.</summary>
    [Serializable]
    public sealed class InteractionEventData
    {
        [SerializeField] private string objectId;
        [SerializeField] private InteractableObjectType objectType;
        [SerializeField] private FishLanternStep step;
        [SerializeField] private InteractionKind interactionKind;
        [SerializeField] private InteractionResult result;
        [SerializeField] private InteractionFailureReason failureReason;
        [SerializeField, Range(0f, 1f)] private float progress;
        [SerializeField] private string targetId;
        [SerializeField] private InteractionHand hand;
        [SerializeField] private Vector3 worldPosition;
        [SerializeField] private double timestamp;
        [SerializeField] private int attempt;
        [SerializeField, TextArea] private string details;

        /// <summary>Stable source object ID.</summary>
        public string ObjectId => objectId;

        /// <summary>Semantic source object type.</summary>
        public InteractableObjectType ObjectType => objectType;

        /// <summary>Step associated with the event.</summary>
        public FishLanternStep Step => step;

        /// <summary>Normalized interaction operation.</summary>
        public InteractionKind InteractionKind => interactionKind;

        /// <summary>Interaction outcome.</summary>
        public InteractionResult Result => result;

        /// <summary>Failure reason, or <see cref="InteractionFailureReason.None"/>.</summary>
        public InteractionFailureReason FailureReason => failureReason;

        /// <summary>Normalized progress from zero to one.</summary>
        public float Progress => progress;

        /// <summary>Optional socket or target ID.</summary>
        public string TargetId => targetId;

        /// <summary>Hand that produced the event.</summary>
        public InteractionHand Hand => hand;

        /// <summary>World-space location of the interaction.</summary>
        public Vector3 WorldPosition => worldPosition;

        /// <summary>Realtime timestamp in seconds.</summary>
        public double Timestamp => timestamp;

        /// <summary>One-based attempt number when relevant.</summary>
        public int Attempt => attempt;

        /// <summary>Optional diagnostic detail for logs and tutorial routing.</summary>
        public string Details => details;

        /// <summary>Creates an immutable interaction payload.</summary>
        public InteractionEventData(
            string objectId,
            InteractableObjectType objectType,
            FishLanternStep step,
            InteractionKind interactionKind,
            InteractionResult result,
            InteractionFailureReason failureReason = InteractionFailureReason.None,
            float progress = 0f,
            string targetId = "",
            InteractionHand hand = InteractionHand.Unknown,
            Vector3 worldPosition = default,
            int attempt = 0,
            string details = "")
        {
            this.objectId = objectId ?? string.Empty;
            this.objectType = objectType;
            this.step = step;
            this.interactionKind = interactionKind;
            this.result = result;
            this.failureReason = failureReason;
            this.progress = Mathf.Clamp01(progress);
            this.targetId = targetId ?? string.Empty;
            this.hand = hand;
            this.worldPosition = worldPosition;
            timestamp = Time.realtimeSinceStartupAsDouble;
            this.attempt = Mathf.Max(0, attempt);
            this.details = details ?? string.Empty;
        }
    }

    /// <summary>Request sent to a tutorial presenter without embedding tutorial content in interactions.</summary>
    [Serializable]
    public struct TutorialPromptRequest
    {
        [SerializeField] private string promptKey;
        [SerializeField] private string objectId;
        [SerializeField] private FishLanternStep step;
        [SerializeField] private InteractionFailureReason reason;

        /// <summary>Localization or tutorial-content lookup key.</summary>
        public string PromptKey => promptKey;

        /// <summary>Object that should be emphasized, if any.</summary>
        public string ObjectId => objectId;

        /// <summary>Step that requested the prompt.</summary>
        public FishLanternStep Step => step;

        /// <summary>Related failure reason, if any.</summary>
        public InteractionFailureReason Reason => reason;

        /// <summary>Creates a tutorial prompt request.</summary>
        public TutorialPromptRequest(
            string promptKey,
            string objectId,
            FishLanternStep step,
            InteractionFailureReason reason = InteractionFailureReason.None)
        {
            this.promptKey = promptKey ?? string.Empty;
            this.objectId = objectId ?? string.Empty;
            this.step = step;
            this.reason = reason;
        }
    }
}
