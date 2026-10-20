using UnityEngine;

namespace FishLantern.XR
{
    /// <summary>Marks the central table area where a preparation-stage object may be released and kept.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class WorktablePlacementZone : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float horizontalPadding = 0.01f;
        [SerializeField, Range(0.1f, 1f)] private float minimumHorizontalOverlap = 0.65f;
        [SerializeField, Min(0f)] private float maximumReleaseHeight = 0.35f;
        [SerializeField, Min(0f)] private float surfaceClearance = 0.003f;

        private BoxCollider zoneCollider;

        /// <summary>Returns true when the solid footprint of the target fits inside this work area.</summary>
        public bool ContainsFootprint(Transform target)
        {
            return TryGetPlacementPosition(target, out _);
        }

        /// <summary>
        /// Resolves a stable tabletop pose when the object's centre is inside the work area and
        /// most of its footprint overlaps it. Requiring the entire bounds made long final FBX
        /// assets practically impossible to release because a few millimetres over the border
        /// caused an immediate return.
        /// </summary>
        public bool TryGetPlacementPosition(Transform target, out Vector3 placementPosition)
        {
            placementPosition = target != null ? target.position : default;
            if (target == null)
                return false;

            // XR Interaction Toolkit can move the transform and release it in the same frame.
            // Unity's collider bounds may still describe the previous pose until the physics
            // transform sync runs, which used to make a valid centre-table release look as if
            // the object were still sitting in the right-hand material area.
            Physics.SyncTransforms();
            var targetBounds = CalculateSolidBounds(target);
            var zoneBounds = ResolveCollider().bounds;
            var minimumX = zoneBounds.min.x + horizontalPadding;
            var maximumX = zoneBounds.max.x - horizontalPadding;
            var minimumZ = zoneBounds.min.z + horizontalPadding;
            var maximumZ = zoneBounds.max.z - horizontalPadding;

            if (targetBounds.center.x < minimumX || targetBounds.center.x > maximumX ||
                targetBounds.center.z < minimumZ || targetBounds.center.z > maximumZ ||
                targetBounds.min.y < zoneBounds.max.y - 0.04f ||
                targetBounds.min.y > zoneBounds.max.y + maximumReleaseHeight)
            {
                return false;
            }

            var overlapX = Mathf.Max(0f, Mathf.Min(targetBounds.max.x, maximumX) -
                                          Mathf.Max(targetBounds.min.x, minimumX));
            var overlapZ = Mathf.Max(0f, Mathf.Min(targetBounds.max.z, maximumZ) -
                                          Mathf.Max(targetBounds.min.z, minimumZ));
            var ratioX = overlapX / Mathf.Max(0.0001f, targetBounds.size.x);
            var ratioZ = overlapZ / Mathf.Max(0.0001f, targetBounds.size.z);
            if (ratioX < minimumHorizontalOverlap || ratioZ < minimumHorizontalOverlap)
                return false;

            var leftExtent = Mathf.Max(0f, target.position.x - targetBounds.min.x);
            var rightExtent = Mathf.Max(0f, targetBounds.max.x - target.position.x);
            var backExtent = Mathf.Max(0f, target.position.z - targetBounds.min.z);
            var frontExtent = Mathf.Max(0f, targetBounds.max.z - target.position.z);
            placementPosition.x = ClampAxis(
                target.position.x,
                minimumX + leftExtent,
                maximumX - rightExtent,
                zoneBounds.center.x);
            placementPosition.z = ClampAxis(
                target.position.z,
                minimumZ + backExtent,
                maximumZ - frontExtent,
                zoneBounds.center.z);
            placementPosition.y = zoneBounds.max.y + surfaceClearance +
                                  Mathf.Max(0f, target.position.y - targetBounds.min.y);
            return true;
        }

        private BoxCollider ResolveCollider()
        {
            zoneCollider ??= GetComponent<BoxCollider>();
            return zoneCollider;
        }

        private static Bounds CalculateSolidBounds(Transform target)
        {
            var colliders = target.GetComponentsInChildren<Collider>(true);
            var hasBounds = false;
            var bounds = new Bounds(target.position, Vector3.zero);
            foreach (var candidate in colliders)
            {
                if (candidate == null || !candidate.enabled || candidate.isTrigger)
                    continue;

                if (!hasBounds)
                {
                    bounds = candidate.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(candidate.bounds);
                }
            }

            return bounds;
        }

        private static float ClampAxis(float value, float minimum, float maximum, float fallback)
        {
            return minimum <= maximum ? Mathf.Clamp(value, minimum, maximum) : fallback;
        }

        private void OnValidate()
        {
            horizontalPadding = Mathf.Max(0f, horizontalPadding);
            minimumHorizontalOverlap = Mathf.Clamp01(minimumHorizontalOverlap);
            maximumReleaseHeight = Mathf.Max(0f, maximumReleaseHeight);
            surfaceClearance = Mathf.Max(0f, surfaceClearance);
        }
    }
}
