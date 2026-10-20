using System;
using FishLantern.XR;
using UnityEngine;

namespace Yudeng.Presentation
{
    [Serializable]
    public sealed class DisplaySectionBinding
    {
        [SerializeField] private Transform pivot;
        [SerializeField] private Transform[] members = Array.Empty<Transform>();
        [SerializeField, Range(0f, 12f)] private float maximumAngle = 6f;
        [SerializeField, Range(0.05f, 0.6f)] private float responseSeconds = 0.2f;
        public Transform Pivot => pivot;
        public Transform[] Members => members;
        public float MaximumAngle => maximumAngle;
        public float ResponseSeconds => responseSeconds;

        public DisplaySectionBinding(Transform rotationPivot, Transform[] actualMembers,
            float angleLimit, float delaySeconds)
        {
            pivot = rotationPivot;
            members = actualMembers ?? Array.Empty<Transform>();
            maximumAngle = Mathf.Clamp(angleLimit, 0f, 12f);
            responseSeconds = Mathf.Clamp(delaySeconds, 0.05f, 0.6f);
        }
    }

    /// <summary>Moves the existing made fish with its held rod; never changes crafting completion.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(300)]
    public sealed class FishLanternDisplayMotion : MonoBehaviour, IInteractionResettable
    {
        private const string RuntimeBambooVisualName = "竹条弯曲模型";

        [SerializeField] private MonoBehaviour stepSource;
        [SerializeField] private LampDisplayPresenter lamp;
        [SerializeField] private GrabInteractableEx rod;
        [SerializeField] private Transform artworkRoot;
        [SerializeField] private Transform rodMount;
        [SerializeField] private Transform artworkMount;
        [SerializeField] private GameObject[] workbenchStands = Array.Empty<GameObject>();
        [SerializeField] private DisplaySectionBinding[] sections = Array.Empty<DisplaySectionBinding>();
        [SerializeField, Min(0)] private int frontBodySectionIndex = 1;
        [SerializeField, Range(0.05f, 1f)] private float liftResponseSeconds = 0.32f;
        [SerializeField, Range(0f, 30f)] private float maximumWholeTilt = 18f;

        private IInteractionStepProvider provider;
        private Rigidbody artworkBody;
        private sealed class MemberPose
        {
            public Transform Member;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 FramePosition;
            public Quaternion FrameRotation;
        }
        private MemberPose[][] initialSectionPoses;
        private float[] currentSectionAngles;
        private bool sectionPosesCaptured;
        private bool installationPoseCaptured;
        private bool suspended;
        private bool wasHeld;
        private bool hasFollowedRod;
        private bool liftingFromWorkbench;
        private float liftElapsedSeconds;
        private Vector3 liftStartPosition;
        private bool hasPreviousRodPosition;
        private Vector3 installationPosition;
        private Quaternion installationRotation;
        private Quaternion neutralArtworkRotation;
        private Quaternion neutralRodRotation;
        private Vector3 previousRodPosition;
        private Vector3 mountOffset;
        private float filteredSideSpeed;

        public bool IsFollowingRod => wasHeld && !suspended;
        public bool HasLiftedArtwork => hasFollowedRod;

        /// <summary>Section order is head, front body, rear body, tail. Each root carries its real paper and ink.</summary>
        public void Configure(MonoBehaviour steps, LampDisplayPresenter presenter, GrabInteractableEx displayRod,
            Transform actualArtwork, Transform rodConnection, Transform artworkConnection,
            GameObject[] stands, DisplaySectionBinding[] articulatedSections)
        {
            Unsubscribe();
            stepSource = steps;
            lamp = presenter;
            rod = displayRod;
            artworkRoot = actualArtwork;
            rodMount = rodConnection;
            artworkMount = artworkConnection;
            workbenchStands = stands ?? Array.Empty<GameObject>();
            sections = articulatedSections ?? Array.Empty<DisplaySectionBinding>();
            sectionPosesCaptured = false;
            if (isActiveAndEnabled) Subscribe();
            ApplyStep(CurrentStep);
        }

        public void SetSuspended(bool value)
        {
            suspended = value;
            ClearMotionContinuity();
        }

        /// <summary>For pause, focus changes and hand changes; does not move the work or clear its progress.</summary>
        public void ClearMotionContinuity()
        {
            hasPreviousRodPosition = false;
            filteredSideSpeed = 0f;
            wasHeld = false;
        }

        /// <summary>Returns the same completed work to its recorded workbench pose after rod recovery.</summary>
        public void ReturnToWorkbench()
        {
            ClearMotionContinuity();
            hasFollowedRod = false;
            liftingFromWorkbench = false;
            liftElapsedSeconds = 0f;
            RestoreSections();
            if (installationPoseCaptured && artworkRoot != null)
                SetArtworkPose(installationPosition, installationRotation);
            SetStandsVisible(CurrentStep is FishLanternStep.Papering or FishLanternStep.SketchAndPaint || IsDisplayStep);
        }

        public void ResetInteraction()
        {
            ClearMotionContinuity();
            suspended = false;
            hasFollowedRod = false;
            liftingFromWorkbench = false;
            liftElapsedSeconds = 0f;
            installationPoseCaptured = false;
            RestoreSections();
            sectionPosesCaptured = false;
            // The existing GrabInteractableEx reset owns the original frame pose and socket state.
        }

        private FishLanternStep CurrentStep =>
            (provider ?? stepSource as IInteractionStepProvider)?.CurrentStep ?? FishLanternStep.None;
        private bool IsDisplayStep => CurrentStep is FishLanternStep.LampDisplay or FishLanternStep.Complete;

        private void OnEnable()
        {
            Subscribe();
            ApplyStep(CurrentStep);
        }
        private void OnDisable()
        {
            Unsubscribe();
            ClearMotionContinuity();
        }

        private void Subscribe()
        {
            Unsubscribe();
            provider = stepSource as IInteractionStepProvider;
            provider?.StepChanged.AddListener(ApplyStep);
            if (lamp != null) lamp.StateChanged += HandleLampState;
        }
        private void Unsubscribe()
        {
            provider?.StepChanged.RemoveListener(ApplyStep);
            provider = null;
            if (lamp != null) lamp.StateChanged -= HandleLampState;
        }

        private void ApplyStep(FishLanternStep step)
        {
            var visible = step is FishLanternStep.LampDisplay or FishLanternStep.Complete;
            if (rod != null && rod.gameObject.activeSelf != visible) rod.gameObject.SetActive(visible);
            if (step is FishLanternStep.None or FishLanternStep.Welcome) ResetInteraction();
            // The support belongs to paper work and display; keep the bamboo bending area open.
            SetStandsVisible(step is FishLanternStep.Papering or FishLanternStep.SketchAndPaint || visible && !hasFollowedRod);
            if (!visible) ClearMotionContinuity();
        }

        private void HandleLampState()
        {
            if (lamp != null && lamp.IsInstalled && !installationPoseCaptured && artworkRoot != null)
            {
                installationPosition = artworkRoot.position;
                installationRotation = artworkRoot.rotation;
                installationPoseCaptured = true;
                // Capture the actual assembled state, after sockets and paper attachment have finished.
                sectionPosesCaptured = false;
                CaptureSections();
            }
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying || !IsDisplayStep || lamp == null || !lamp.IsInstalled ||
                rod == null || artworkRoot == null || rodMount == null || artworkMount == null)
                return;
            if (suspended || !Application.isFocused)
            {
                ClearMotionContinuity();
                return;
            }
            HandleLampState();
            var held = rod.IsHeldByHand;
            var seconds = Mathf.Min(Time.deltaTime, 0.05f);
            if (seconds <= 0f) return;
            if (!held)
            {
                ClearMotionContinuity();
                // Once mounted, the work remains connected when the rod is put down. A first
                // lift already in progress may finish, while pause still freezes both objects.
                if (hasFollowedRod) UpdateArtworkAttachment(seconds);
                UpdateSections(0f, seconds);
                return;
            }
            if (!wasHeld)
            {
                if (!hasFollowedRod)
                {
                    liftingFromWorkbench = true;
                    liftElapsedSeconds = 0f;
                    liftStartPosition = artworkRoot.position;
                }
                neutralArtworkRotation = artworkRoot.rotation;
                neutralRodRotation = rod.transform.rotation;
                mountOffset = Vector3.Scale(artworkRoot.InverseTransformPoint(artworkMount.position), artworkRoot.lossyScale);
                previousRodPosition = rodMount.position;
                hasPreviousRodPosition = false;
                wasHeld = true;
                hasFollowedRod = true;
                SetStandsVisible(false);
            }
            UpdateArtworkAttachment(seconds);

            var sideSpeed = hasPreviousRodPosition
                ? Vector3.Dot((rodMount.position - previousRodPosition) / seconds, artworkRoot.right) : 0f;
            previousRodPosition = rodMount.position;
            hasPreviousRodPosition = true;
            filteredSideSpeed = Mathf.Lerp(filteredSideSpeed, Mathf.Clamp(sideSpeed, -1.5f, 1.5f),
                1f - Mathf.Exp(-seconds / 0.1f));
            UpdateSections(Mathf.Clamp(-filteredSideSpeed * 2f, -1f, 1f), seconds);
        }

        private void UpdateArtworkAttachment(float seconds)
        {
            var rotationDelta = rod.transform.rotation * Quaternion.Inverse(neutralRodRotation);
            var angle = Quaternion.Angle(Quaternion.identity, rotationDelta);
            var limitedDelta = angle > maximumWholeTilt && angle > 0.001f
                ? Quaternion.Slerp(Quaternion.identity, rotationDelta, maximumWholeTilt / angle) : rotationDelta;
            var desiredRotation = limitedDelta * neutralArtworkRotation;
            var rotationResponse = 1f - Mathf.Exp(-seconds / Mathf.Max(0.05f, liftResponseSeconds));
            var appliedRotation = Quaternion.Slerp(artworkRoot.rotation, desiredRotation, rotationResponse);
            // Solve position from the rotation actually used this frame. Continuing to interpolate
            // position would separate the saddle and the fish abdomen during every change of direction.
            var connectedPosition = rodMount.position - appliedRotation * mountOffset;
            if (liftingFromWorkbench)
            {
                liftElapsedSeconds += seconds;
                var amount = Mathf.Clamp01(liftElapsedSeconds / Mathf.Max(0.05f, liftResponseSeconds));
                var easedAmount = amount * amount * (3f - 2f * amount);
                connectedPosition = Vector3.Lerp(liftStartPosition, connectedPosition, easedAmount);
                if (amount >= 1f) liftingFromWorkbench = false;
            }
            SetArtworkPose(connectedPosition, appliedRotation);
        }

        private void CaptureSections()
        {
            if (sectionPosesCaptured || artworkRoot == null) return;
            initialSectionPoses = new MemberPose[sections.Length][];
            currentSectionAngles = new float[sections.Length];
            // BambooStripDeformation creates the player's real front ring during Play. It cannot
            // be a serialized scene reference, so capture that existing visual once installation begins.
            var bentBamboo = artworkRoot.Find(RuntimeBambooVisualName);
            var includeBentBamboo = bentBamboo != null && !IsAlreadyBound(bentBamboo);
            for (var index = 0; index < sections.Length; index++)
            {
                var members = sections[index]?.Members ?? Array.Empty<Transform>();
                if (includeBentBamboo && index == frontBodySectionIndex && sections[index]?.Pivot != null)
                {
                    var withBentBamboo = new Transform[members.Length + 1];
                    Array.Copy(members, withBentBamboo, members.Length);
                    withBentBamboo[members.Length] = bentBamboo;
                    members = withBentBamboo;
                }
                initialSectionPoses[index] = new MemberPose[members.Length];
                for (var memberIndex = 0; memberIndex < members.Length; memberIndex++)
                {
                    var member = members[memberIndex];
                    if (member == null) continue;
                    initialSectionPoses[index][memberIndex] = new MemberPose
                    {
                        Member = member,
                        LocalPosition = member.localPosition,
                        LocalRotation = member.localRotation,
                        FramePosition = artworkRoot.InverseTransformPoint(member.position),
                        FrameRotation = Quaternion.Inverse(artworkRoot.rotation) * member.rotation,
                    };
                }
            }
            artworkBody = artworkRoot.GetComponent<Rigidbody>();
            sectionPosesCaptured = true;
        }

        private bool IsAlreadyBound(Transform candidate)
        {
            foreach (var section in sections)
            {
                if (section?.Members == null) continue;
                foreach (var member in section.Members)
                    if (member != null && (candidate == member || candidate.IsChildOf(member))) return true;
            }
            return false;
        }

        private void RestoreSections()
        {
            if (!sectionPosesCaptured) return;
            for (var index = 0; index < sections.Length; index++)
            {
                currentSectionAngles[index] = 0f;
                foreach (var pose in initialSectionPoses[index])
                    if (pose?.Member != null)
                        pose.Member.SetLocalPositionAndRotation(pose.LocalPosition, pose.LocalRotation);
            }
        }

        private void UpdateSections(float response, float seconds)
        {
            CaptureSections();
            if (!sectionPosesCaptured) return;
            for (var index = 0; index < sections.Length; index++)
            {
                var section = sections[index];
                if (section?.Pivot == null) continue;
                currentSectionAngles[index] = Mathf.Lerp(currentSectionAngles[index], response * section.MaximumAngle,
                    1f - Mathf.Exp(-seconds / Mathf.Max(0.05f, section.ResponseSeconds)));
                var rotation = Quaternion.AngleAxis(currentSectionAngles[index], Vector3.up);
                var pivot = artworkRoot.InverseTransformPoint(section.Pivot.position);
                foreach (var pose in initialSectionPoses[index])
                {
                    if (pose?.Member == null) continue;
                    // Members keep their original parents and visibility controllers. PaperPaintSurface
                    // and every actual stroke follow their own fitted-paper member without a duplicate mesh.
                    pose.Member.SetPositionAndRotation(
                        artworkRoot.TransformPoint(pivot + rotation * (pose.FramePosition - pivot)),
                        artworkRoot.rotation * rotation * pose.FrameRotation);
                }
            }
        }

        private void SetArtworkPose(Vector3 position, Quaternion rotation)
        {
            artworkRoot.SetPositionAndRotation(position, rotation);
            if (artworkBody != null)
            {
                artworkBody.position = position;
                artworkBody.rotation = rotation;
            }
        }

        private void SetStandsVisible(bool visible)
        {
            foreach (var stand in workbenchStands)
                if (stand != null && stand.activeSelf != visible) stand.SetActive(visible);
        }
    }
}
