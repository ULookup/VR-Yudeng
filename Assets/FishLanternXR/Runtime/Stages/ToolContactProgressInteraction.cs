using System.Collections.Generic;
using UnityEngine;

namespace FishLantern.XR
{
    /// <summary>Accumulates progress while an accepted tool remains inside a work-zone trigger.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(InteractableIdentity), typeof(InteractionGate), typeof(Collider))]
    public sealed class ToolContactProgressInteraction : MonoBehaviour, IInteractionResettable
    {
        [SerializeField] private List<InteractableObjectType> acceptedToolTypes = new();
        [SerializeField] private InteractionKind progressKind = InteractionKind.GlueProgress;
        [SerializeField] private InteractionKind completionKind = InteractionKind.GlueApplied;
        [SerializeField, Min(0.05f)] private float requiredContactSeconds = 1.5f;
        [SerializeField] private EventPrerequisite prerequisite;
        [SerializeField] private bool requireTriggerActivation;
        [SerializeField] private bool requireToolContactPoint;
        [SerializeField, Min(1)] private int requiredRegionCount = 1;
        [SerializeField] private bool preserveAppliedCoverage;
        [SerializeField] private InteractionEventUnityEvent onProgress = new();
        [SerializeField] private InteractionEventUnityEvent onCompleted = new();

        private InteractableIdentity identity;
        private InteractionGate gate;
        private float lastReportedProgress;
        private bool completed;
        private bool applicationPaused;
        private RegionState[] regions;

        private sealed class RegionState
        {
            public float ContactSeconds;
            public readonly Dictionary<Collider, GrabInteractableEx> Contacts = new();
        }

        /// <summary>Current normalized contact progress.</summary>
        public float Progress
        {
            get
            {
                if (completed) return 1f;
                EnsureRegions();
                var total = 0f;
                for (var index = 0; index < regions.Length; index++) total += GetRegionProgress(index);
                return total / regions.Length;
            }
        }

        public int RegionCount => Mathf.Max(1, requiredRegionCount);

        public int CompletedRegionCount
        {
            get
            {
                EnsureRegions();
                var count = 0;
                for (var index = 0; index < regions.Length; index++)
                    if (GetRegionProgress(index) >= 1f) count++;
                return count;
            }
        }

        public bool IsComplete => completed;

        /// <summary>Reads local coverage for the region's actual wet-glue visual.</summary>
        public float GetRegionProgress(int index)
        {
            EnsureRegions();
            return index < 0 || index >= regions.Length ? 0f :
                Mathf.Clamp01(regions[index].ContactSeconds / Mathf.Max(0.05f, requiredContactSeconds));
        }

        /// <summary>Raised for throttled normalized progress updates.</summary>
        public InteractionEventUnityEvent OnProgress => onProgress;

        /// <summary>Raised once when required contact is reached.</summary>
        public InteractionEventUnityEvent OnCompleted => onCompleted;

        /// <summary>Separates a tool's effective working end from its generous grab collider.</summary>
        public void ConfigureWorkingEnd(bool required) => requireToolContactPoint = required;

        /// <summary>Requires physical contact in every independent region before publishing completion.</summary>
        public void ConfigureCoverage(int regionCount, bool requireActivation = true, bool preserveCoverage = true)
        {
            requiredRegionCount = Mathf.Max(1, regionCount);
            requireTriggerActivation = requireActivation;
            preserveAppliedCoverage = preserveCoverage;
            EnsureRegions();
            ConfigureRootCollider();
        }

        /// <summary>Configures accepted tools, events, duration and optional prerequisite.</summary>
        public void Configure(
            IEnumerable<InteractableObjectType> toolTypes,
            InteractionKind progressEvent,
            InteractionKind completionEvent,
            float seconds,
            EventPrerequisite requiredEvent = null)
        {
            acceptedToolTypes = toolTypes == null
                ? new List<InteractableObjectType>()
                : new List<InteractableObjectType>(toolTypes);
            progressKind = progressEvent;
            completionKind = completionEvent;
            requiredContactSeconds = Mathf.Max(0.05f, seconds);
            prerequisite = requiredEvent;
        }

        /// <summary>Adds one contact sample after the physical tool conditions have been satisfied.</summary>
        public float AddProgress(float deltaSeconds)
        {
            // Regional work can only accumulate through the corresponding physical region.
            if (RegionCount > 1) return Progress;
            return AddRegionProgress(0, deltaSeconds);
        }

        private float AddRegionProgress(int regionIndex, float deltaSeconds)
        {
            EnsureReferences();
            if (completed || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds) ||
                deltaSeconds <= 0f || (prerequisite != null && !prerequisite.IsSatisfied))
                return Progress;
            if (!gate.CanPerform(progressKind, reportDenial: false))
                return Progress;

            EnsureRegions();
            var region = regions[regionIndex];
            if (region.ContactSeconds >= requiredContactSeconds) return Progress;
            region.ContactSeconds = Mathf.Min(requiredContactSeconds, region.ContactSeconds + deltaSeconds);
            var progress = Progress;
            var regionComplete = region.ContactSeconds >= requiredContactSeconds;
            if (progress >= 1f || regionComplete || progress - lastReportedProgress >= 0.1f)
            {
                lastReportedProgress = progress;
                var progressEvent = identity.CreateEvent(
                    progressKind,
                    InteractionResult.Progressed,
                    progress: progress,
                    targetId: RegionCount > 1 ? regionIndex.ToString() : string.Empty);
                InteractionServiceHub.Active?.Report(progressEvent);
                onProgress.Invoke(progressEvent);
            }

            if (progress >= 1f)
                Complete();
            return Progress;
        }

        /// <summary>Clears accumulated contact and completion state.</summary>
        public void ResetInteraction()
        {
            EnsureRegions();
            foreach (var region in regions)
            {
                region.ContactSeconds = 0f;
                region.Contacts.Clear();
            }
            lastReportedProgress = 0f;
            completed = false;
        }

        private void Awake()
        {
            EnsureReferences();
            EnsureRegions();
            ConfigureRootCollider();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (RegionCount == 1) TrackRegionContact(0, other, true);
        }

        private void OnTriggerStay(Collider other)
        {
            if (RegionCount == 1) TrackRegionContact(0, other);
        }

        private void OnTriggerExit(Collider other)
        {
            if (RegionCount == 1) RemoveRegionContact(0, other);
        }

        /// <summary>Called by a small work-region trigger, never by a tool's large grab collider.</summary>
        public void TrackRegionContact(int regionIndex, Collider other, bool reportDenial = false)
        {
            if (!isActiveAndEnabled || completed || applicationPaused || !Application.isFocused ||
                other == null || regionIndex < 0 || regionIndex >= RegionCount)
                return;
            EnsureReferences();
            EnsureRegions();
            if (requireToolContactPoint && other.GetComponent<ToolContactPoint>() == null) return;
            var tool = other.GetComponentInParent<InteractableIdentity>();
            if (!Accepts(tool))
            {
                if (reportDenial && tool != null && other.GetComponentInParent<GrabInteractableEx>() is { IsHeldByHand: true })
                    gate.Deny(progressKind, InteractionFailureReason.WrongObject, details: "Wrong tool entered work zone.");
                return;
            }

            var contacts = regions[regionIndex].Contacts;
            if (!contacts.ContainsKey(other)) contacts.Add(other, tool.GetComponent<GrabInteractableEx>());

            if (reportDenial && prerequisite != null && !prerequisite.IsSatisfied)
                gate.Deny(progressKind, InteractionFailureReason.PrerequisiteMissing);
        }

        /// <summary>Ends only the departing region contact, retaining glue already applied.</summary>
        public void RemoveRegionContact(int regionIndex, Collider other)
        {
            if (regions == null || regionIndex < 0 || regionIndex >= regions.Length || other == null) return;
            var region = regions[regionIndex];
            region.Contacts.Remove(other);
            if (!HasActiveTool(region)) ResetContactProgress(region);
        }

        public void ClearRegionContacts(int regionIndex)
        {
            if (regions == null || regionIndex < 0 || regionIndex >= regions.Length) return;
            regions[regionIndex].Contacts.Clear();
            ResetContactProgress(regions[regionIndex]);
        }

        private void FixedUpdate()
        {
            if (completed) return;
            EnsureRegions();
            if (applicationPaused || !Application.isFocused)
            {
                ClearPhysicalContacts();
                return;
            }
            // A physical sample belongs to the work zone, not to each overlapping tool collider.
            for (var index = 0; index < regions.Length; index++)
            {
                var region = regions[index];
                if (!HasActiveTool(region) ||
                    (prerequisite != null && !prerequisite.IsSatisfied))
                {
                    ResetContactProgress(region);
                    continue;
                }
                AddRegionProgress(index, Time.fixedDeltaTime);
            }
        }

        private bool HasActiveTool(RegionState region)
        {
            foreach (var contact in region.Contacts)
            {
                if (contact.Key == null || !contact.Key.enabled || !contact.Key.gameObject.activeInHierarchy)
                    continue;
                var tool = contact.Value;
                if (tool != null && tool.IsHeldByHand &&
                    (!(requireTriggerActivation || completionKind == InteractionKind.TapeFixed) || tool.TriggerPressed))
                    return true;
            }
            return false;
        }

        private void ResetContactProgress(RegionState region)
        {
            if (preserveAppliedCoverage || completed || region.ContactSeconds <= 0f) return;
            region.ContactSeconds = 0f;
            lastReportedProgress = Progress;
            EnsureReferences();
            var reset = identity.CreateEvent(progressKind, InteractionResult.Progressed, progress: Progress);
            InteractionServiceHub.Active?.Report(reset);
            onProgress.Invoke(reset);
        }

        private void OnDisable() => ClearPhysicalContacts();

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) ClearPhysicalContacts();
        }

        private void OnApplicationPause(bool paused)
        {
            applicationPaused = paused;
            if (paused) ClearPhysicalContacts();
        }

        private void ClearPhysicalContacts()
        {
            if (regions == null) return;
            foreach (var region in regions)
            {
                region.Contacts.Clear();
                ResetContactProgress(region);
            }
        }

        private bool Accepts(InteractableIdentity tool)
        {
            return tool != null &&
                   (acceptedToolTypes.Count == 0 || acceptedToolTypes.Contains(tool.ObjectType));
        }

        private void Complete()
        {
            if (completed)
                return;

            completed = true;
            var eventData = identity.CreateEvent(
                completionKind,
                InteractionResult.Succeeded,
                progress: 1f);
            var hub = InteractionServiceHub.Active;
            hub?.Report(eventData);
            hub?.FeedbackService?.PlaySuccess(identity, completionKind);
            onCompleted.Invoke(eventData);
        }

        private void EnsureReferences()
        {
            if (identity == null) identity = GetComponent<InteractableIdentity>();
            if (gate == null) gate = GetComponent<InteractionGate>();
        }

        private void EnsureRegions()
        {
            if (regions != null && regions.Length == RegionCount) return;
            regions = new RegionState[RegionCount];
            for (var index = 0; index < regions.Length; index++) regions[index] = new RegionState();
        }

        private void ConfigureRootCollider()
        {
            // Regional children own coverage. A shared paper root may still need a solid grab collider.
            var rootCollider = GetComponent<Collider>();
            if (rootCollider != null && (RegionCount == 1 || GetComponent<GrabInteractableEx>() == null))
                rootCollider.isTrigger = true;
        }

        private void OnValidate()
        {
            requiredContactSeconds = Mathf.Max(0.05f, requiredContactSeconds);
            requiredRegionCount = Mathf.Max(1, requiredRegionCount);
            ConfigureRootCollider();
        }
    }
}
