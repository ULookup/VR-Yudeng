using UnityEngine;

namespace FishLantern.XR
{
    /// <summary>Forwards physical tool visits to an ordered drawing or cutting path.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class PathCheckpointTrigger : MonoBehaviour
    {
        [SerializeField] private PathCheckpointInteraction owner;
        [SerializeField, Min(0)] private int checkpointIndex;
        [SerializeField] private bool requireToolContactPoint;

        public void Configure(PathCheckpointInteraction pathOwner, int index, bool requireWorkingEnd = false)
        {
            owner = pathOwner;
            checkpointIndex = Mathf.Max(0, index);
            requireToolContactPoint = requireWorkingEnd;
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other) => Visit(other);
        private void OnTriggerStay(Collider other) => Visit(other);

        private void Visit(Collider other)
        {
            if (owner == null || !owner.isActiveAndEnabled || other == null ||
                (requireToolContactPoint && other.GetComponent<ToolContactPoint>() == null)) return;
            owner?.TryVisit(checkpointIndex, other.GetComponentInParent<InteractableIdentity>());
        }

        private void Reset() => GetComponent<Collider>().isTrigger = true;
    }
}
