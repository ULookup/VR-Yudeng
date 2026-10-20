using UnityEngine;

namespace FishLantern.XR
{
    /// <summary>
    /// Keeps replaceable art beneath a stable interaction root so art swaps do not change behavior components.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ReplaceableVisualSlot : MonoBehaviour
    {
        [SerializeField] private Transform visualAnchor;
        [SerializeField] private GameObject currentVisual;

        /// <summary>Stable child transform under which final artwork is instantiated.</summary>
        public Transform VisualAnchor => visualAnchor;

        /// <summary>Currently active placeholder or final visual instance.</summary>
        public GameObject CurrentVisual => currentVisual;

        /// <summary>Assigns the stable anchor and current visual from editor tooling.</summary>
        public void Configure(Transform anchor, GameObject visual)
        {
            visualAnchor = anchor;
            currentVisual = visual;
        }

        /// <summary>
        /// Replaces only the visual child while preserving identity, physics, grab points, sockets and scripts.
        /// </summary>
        public GameObject ReplaceVisual(GameObject visualPrefab)
        {
            if (visualAnchor == null)
                visualAnchor = transform;

            if (currentVisual != null)
            {
                if (Application.isPlaying)
                    Destroy(currentVisual);
                else
                    DestroyImmediate(currentVisual);
            }

            currentVisual = visualPrefab != null
                ? Instantiate(visualPrefab, visualAnchor)
                : null;
            if (currentVisual != null)
            {
                currentVisual.name = visualPrefab.name;
                currentVisual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                currentVisual.transform.localScale = Vector3.one;
            }

            return currentVisual;
        }
    }
}
