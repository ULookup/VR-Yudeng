using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace FishLantern.XR
{
    /// <summary>
    /// Defines the solid tabletop used by remote grabs. When a ray-controlled object would move
    /// through the table, its pose is projected back onto the usable tabletop surface.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class WorktableSurfaceBoundary : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float edgePadding = 0.01f;
        [SerializeField, Min(0f)] private float surfaceClearance = 0.01f;

        private BoxCollider tableCollider;

        /// <summary>The world-space solid bounds of the worktable.</summary>
        public Bounds SurfaceBounds => ResolveCollider().bounds;

        /// <summary>The world-space height where movable assets may rest.</summary>
        public float SurfaceY => SurfaceBounds.max.y;

        /// <summary>
        /// Keeps a remotely grabbed object above and within the tabletop. If the ray points below
        /// the table, the object is projected to the ray/table intersection instead of following
        /// the ray through the solid surface.
        /// </summary>
        public void ConstrainRemoteGrab(Transform target, XRRayInteractor rayInteractor)
        {
            if (target == null || rayInteractor == null)
                return;

            var objectBounds = CalculateSolidBounds(target);
            var tableBounds = SurfaceBounds;
            var offsetFromCenterToBottom = Mathf.Max(0f, target.position.y - objectBounds.min.y);
            var minimumCenterY = tableBounds.max.y + surfaceClearance + offsetFromCenterToBottom;
            var position = target.position;

            if (position.y < minimumCenterY)
            {
                var rayOrigin = rayInteractor.rayOriginTransform != null
                    ? rayInteractor.rayOriginTransform
                    : rayInteractor.transform;
                var direction = rayOrigin.forward;
                if (direction.y < -0.0001f)
                {
                    var distance = (minimumCenterY - rayOrigin.position.y) / direction.y;
                    if (distance >= 0f)
                        position = rayOrigin.position + direction * distance;
                }

                position.y = minimumCenterY;
            }

            var leftExtent = Mathf.Max(0f, target.position.x - objectBounds.min.x);
            var rightExtent = Mathf.Max(0f, objectBounds.max.x - target.position.x);
            var backExtent = Mathf.Max(0f, target.position.z - objectBounds.min.z);
            var frontExtent = Mathf.Max(0f, objectBounds.max.z - target.position.z);

            position.x = ClampAxis(
                position.x,
                tableBounds.min.x + edgePadding + leftExtent,
                tableBounds.max.x - edgePadding - rightExtent);
            position.z = ClampAxis(
                position.z,
                tableBounds.min.z + edgePadding + backExtent,
                tableBounds.max.z - edgePadding - frontExtent);

            target.position = position;
        }

        private BoxCollider ResolveCollider()
        {
            // Unity objects can retain a managed reference after the native object is recreated on leaving Play Mode.
            if (tableCollider == null) tableCollider = GetComponent<BoxCollider>();
            return tableCollider;
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

        private static float ClampAxis(float value, float minimum, float maximum)
        {
            if (minimum > maximum)
                return (minimum + maximum) * 0.5f;
            return Mathf.Clamp(value, minimum, maximum);
        }

        private void OnValidate()
        {
            edgePadding = Mathf.Max(0f, edgePadding);
            surfaceClearance = Mathf.Max(0f, surfaceClearance);
        }
    }
}
