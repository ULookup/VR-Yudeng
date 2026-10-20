using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace FishLantern.XR
{
    /// <summary>Preserves the bamboo centreline length while bending its own mesh and compound colliders.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GrabInteractableEx))]
    public sealed class BambooStripDeformation : MonoBehaviour, IInteractionResettable
    {
        [SerializeField] private Renderer sourceRenderer;
        [SerializeField] private Collider originalCollider;
        [SerializeField] private Vector3 lengthAxis = Vector3.right;
        [SerializeField] private Vector3 bendDirection = Vector3.up;
        [SerializeField, Range(5f, 360f)] private float maximumBendDegrees = 360f;
        [SerializeField, Range(12, 64)] private int colliderSegmentCount = 24;
        [SerializeField, Min(0f)] private float colliderPadding = 0.003f;
        [SerializeField] private Transform collisionRoot;
        [SerializeField] private BoxCollider[] segmentColliders = Array.Empty<BoxCollider>();
        [SerializeField] private Transform seamAnchor;
        [SerializeField] private Transform[] markingPoints = Array.Empty<Transform>();
        [SerializeField] private float[] markingPositions = { 0f, 0.25f, 150f / 360f, 210f / 360f, 0.75f };
        [SerializeField] private float anchorNormalOffset = -0.007f;
        [SerializeField, HideInInspector] private bool originalColliderWasEnabled;

        private Mesh deformedMesh;
        private GameObject deformedVisual;
        private GrabInteractableEx grab;
        private Vector3[] originalVertices;
        private Vector3[] deformedVertices;
        private Vector3[] originalNormals;
        private Vector3[] deformedNormals;
        private Vector4[] originalTangents;
        private Vector4[] deformedTangents;
        private Vector3 centre;
        private Vector3 longitudinal;
        private Vector3 inward;
        private Vector3 planeNormal;
        private float centrelineLength;
        private float radialThickness;
        private float normalThickness;
        private float progress;
        private bool initialized;
        private bool initializationFailed;
        private bool sourceWasEnabled;

        public float Progress => progress;
        public float CenterlineLength => centrelineLength;
        public float FinalRingRadius => centrelineLength / (maximumBendDegrees * Mathf.Deg2Rad);
        public Vector3 FinalRingCenterLocal => centre + inward * FinalRingRadius;
        public Vector3 BendPlaneNormalLocal => planeNormal;
        public bool IsInitialized => initialized;

        /// <summary>
        /// Saves scene configuration and creates reusable collider segments before XRI starts selecting the strip.
        /// Axes are in this component's local coordinates. Existing source materials and UVs are retained.
        /// </summary>
        public void Configure(Renderer bambooRenderer, Collider straightCollider,
            Vector3 longitudinalAxis, Vector3 directionOfBend,
            float bendDegrees = 360f, int collisionSegments = 24)
        {
            if (Application.isPlaying && (initialized || GetComponent<GrabInteractableEx>().isSelected))
                throw new InvalidOperationException("竹条开始交互后不能替换变形配置，请在场景接入阶段配置。");
            if (bambooRenderer == null || straightCollider == null)
                throw new ArgumentException("竹条变形需要模型渲染器和原始直条碰撞体。");
            if (longitudinalAxis.sqrMagnitude < 0.001f ||
                Vector3.Cross(longitudinalAxis, directionOfBend).sqrMagnitude < 0.001f)
                throw new ArgumentException("竹条长度轴与弯曲方向必须非零且不平行。");

            if (originalCollider != straightCollider)
                originalColliderWasEnabled = straightCollider.enabled;
            sourceRenderer = bambooRenderer;
            originalCollider = straightCollider;
            initializationFailed = false;
            lengthAxis = longitudinalAxis.normalized;
            bendDirection = Vector3.ProjectOnPlane(directionOfBend, lengthAxis).normalized;
            maximumBendDegrees = Mathf.Clamp(bendDegrees, 5f, 360f);
            colliderSegmentCount = Mathf.Clamp(collisionSegments, 12, 64);
            grab = GetComponent<GrabInteractableEx>();
            grab.trackScale = false;
            PrepareColliderSegments();

            // These are only the edit-time straight poses. Awake replaces them with the exact baked mesh dimensions.
            ResolveAxes();
            var bounds = sourceRenderer.bounds;
            var localBounds = BoundsInLocalSpace(bounds);
            SetDimensions(localBounds);
            UpdateColliderSegments(0f);
            UpdateAnchors();
        }

        /// <summary>
        /// Binds the existing tape and marking targets without changing their hierarchy or activation ownership.
        /// The caller parents the tape target and the entire marking path under the bamboo object.
        /// </summary>
        public void ConfigureAnchors(Transform tapeSeam, Transform[] checkpoints,
            float normalOffset = -0.007f, float[] normalizedPositions = null)
        {
            seamAnchor = tapeSeam;
            markingPoints = checkpoints == null ? Array.Empty<Transform>() : (Transform[])checkpoints.Clone();
            anchorNormalOffset = normalOffset;
            if (normalizedPositions != null)
            {
                if (normalizedPositions.Length != markingPoints.Length)
                    throw new ArgumentException("标记点数量必须与沿竹条的位置数量相同。");
                markingPositions = (float[])normalizedPositions.Clone();
            }
            else
            {
                markingPositions = new float[markingPoints.Length];
                var fivePointPositions = new[] { 0f, 0.25f, 150f / 360f, 210f / 360f, 0.75f };
                for (var index = 0; index < markingPositions.Length; index++)
                    markingPositions[index] = markingPositions.Length == 5
                        ? fivePointPositions[index]
                        : index / (float)Mathf.Max(1, markingPositions.Length);
            }
            UpdateAnchors();
        }

        /// <summary>Changes geometry only when the normalized bending progress changes.</summary>
        public bool SetProgress(float normalizedProgress)
        {
            if (float.IsNaN(normalizedProgress) || float.IsInfinity(normalizedProgress)) return false;
            if (!EnsureInitialized()) return false;
            var next = Mathf.Clamp01(normalizedProgress);
            if (Mathf.Abs(next - progress) < 0.0001f) return true;
            progress = next;
            UpdateGeometry();
            return true;
        }

        public void ResetInteraction()
        {
            SetProgress(0f);
        }

        /// <summary>Returns a material centreline point in this component's local coordinates.</summary>
        public Vector3 GetCenterlinePoint(float normalizedPosition)
        {
            return PointAt(Mathf.Clamp01(normalizedPosition), progress);
        }

        /// <summary>Returns the material tangent in this component's local coordinates.</summary>
        public Vector3 GetCenterlineTangent(float normalizedPosition)
        {
            var angle = (Mathf.Clamp01(normalizedPosition) - 0.5f) * BendRadians(progress);
            return Mathf.Cos(angle) * longitudinal + Mathf.Sin(angle) * inward;
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        private bool EnsureInitialized()
        {
            if (initialized) return true;
            if (initializationFailed || !Application.isPlaying) return false;
            initializationFailed = true;
            grab = GetComponent<GrabInteractableEx>();
            if (sourceRenderer == null || segmentColliders.Length != colliderSegmentCount)
                return FailInitialization("请先通过 Configure 接入竹条模型和分段碰撞体。");
            var scale = transform.lossyScale;
            if (scale.x <= 0f || Mathf.Abs(scale.x - scale.y) > 0.001f ||
                Mathf.Abs(scale.x - scale.z) > 0.001f)
                return FailInitialization("竹条根物件及父级需要正值等比缩放，才能保持弯曲前后的材料长度。");

            ResolveAxes();
            if (sourceRenderer is SkinnedMeshRenderer skinned)
            {
                // BakeMesh is a native snapshot and also works when the imported shared mesh has Read/Write disabled.
                deformedMesh = new Mesh();
                skinned.BakeMesh(deformedMesh, false);
            }
            else
            {
                var filter = sourceRenderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable)
                    return FailInitialization("静态竹条网格需要可读网格；当前带骨骼的竹条可直接烘焙快照。");
                deformedMesh = Instantiate(filter.sharedMesh);
            }

            deformedMesh.name = "竹条独立变形网格";
            deformedMesh.hideFlags = HideFlags.DontSave;
            deformedMesh.MarkDynamic();
            originalVertices = deformedMesh.vertices;
            if (originalVertices.Length == 0)
                return FailInitialization("竹条渲染器没有可用于变形的顶点。");
            originalNormals = deformedMesh.normals;
            originalTangents = deformedMesh.tangents;
            deformedVertices = new Vector3[originalVertices.Length];
            deformedNormals = new Vector3[originalNormals.Length];
            deformedTangents = new Vector4[originalTangents.Length];

            var sourceToLocal = transform.worldToLocalMatrix * sourceRenderer.transform.localToWorldMatrix;
            var normalToLocal = sourceToLocal.inverse.transpose;
            var bounds = new Bounds(sourceToLocal.MultiplyPoint3x4(originalVertices[0]), Vector3.zero);
            for (var index = 0; index < originalVertices.Length; index++)
            {
                originalVertices[index] = sourceToLocal.MultiplyPoint3x4(originalVertices[index]);
                bounds.Encapsulate(originalVertices[index]);
            }
            for (var index = 0; index < originalNormals.Length; index++)
                originalNormals[index] = normalToLocal.MultiplyVector(originalNormals[index]).normalized;
            for (var index = 0; index < originalTangents.Length; index++)
            {
                var tangent = originalTangents[index];
                var direction = sourceToLocal.MultiplyVector(new Vector3(tangent.x, tangent.y, tangent.z)).normalized;
                originalTangents[index] = new Vector4(direction.x, direction.y, direction.z, tangent.w);
            }
            SetDimensions(bounds);
            SetDimensionsFromVertices();
            if (centrelineLength < 0.01f)
                return FailInitialization("竹条长度小于 1 厘米，请检查模型轴向配置。");

            deformedVisual = new GameObject("竹条弯曲模型") { hideFlags = HideFlags.DontSave };
            deformedVisual.transform.SetParent(transform, false);
            deformedVisual.layer = sourceRenderer.gameObject.layer;
            deformedVisual.AddComponent<MeshFilter>().sharedMesh = deformedMesh;
            var renderer = deformedVisual.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = sourceRenderer.sharedMaterials;
            renderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
            renderer.receiveShadows = sourceRenderer.receiveShadows;
            renderer.lightProbeUsage = sourceRenderer.lightProbeUsage;
            renderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
            renderer.renderingLayerMask = sourceRenderer.renderingLayerMask;
            sourceWasEnabled = sourceRenderer.enabled;
            renderer.enabled = sourceWasEnabled;
            sourceRenderer.enabled = false;
            grab.trackScale = false;
            if (originalCollider != null) originalCollider.enabled = false;
            initialized = true;
            initializationFailed = false;
            progress = 0f;
            UpdateGeometry();
            return true;
        }

        private void PrepareColliderSegments()
        {
            var manager = grab.interactionManager;
            var registered = Application.isPlaying && manager != null &&
                             manager.IsRegistered((IXRInteractable)grab);
            if (registered) manager.UnregisterInteractable((IXRInteractable)grab);
            // Keep an explicit list: the tape and marking trigger children are separate interactions.
            grab.colliders.Clear();
            if (collisionRoot == null)
            {
                collisionRoot = new GameObject("竹条分段碰撞").transform;
                collisionRoot.SetParent(transform, false);
            }
            var previous = segmentColliders;
            segmentColliders = new BoxCollider[colliderSegmentCount];
            for (var index = 0; index < colliderSegmentCount; index++)
            {
                var segment = index < previous.Length ? previous[index] : null;
                if (segment == null)
                {
                    var part = new GameObject($"竹条碰撞段 {index + 1}");
                    part.transform.SetParent(collisionRoot, false);
                    segment = part.AddComponent<BoxCollider>();
                }
                segment.gameObject.layer = gameObject.layer;
                segment.isTrigger = false;
                segment.enabled = true;
                segment.sharedMaterial = originalCollider.sharedMaterial;
                segmentColliders[index] = segment;
                grab.colliders.Add(segment);
            }
            for (var index = colliderSegmentCount; index < previous.Length; index++)
            {
                if (previous[index] == null) continue;
                previous[index].enabled = false;
                if (Application.isPlaying) Destroy(previous[index].gameObject);
                else DestroyImmediate(previous[index].gameObject);
            }
            originalCollider.enabled = false;
            if (registered) manager.RegisterInteractable((IXRInteractable)grab);
        }

        private void ResolveAxes()
        {
            longitudinal = lengthAxis.normalized;
            inward = Vector3.ProjectOnPlane(bendDirection, longitudinal).normalized;
            planeNormal = Vector3.Cross(longitudinal, inward).normalized;
        }

        private Bounds BoundsInLocalSpace(Bounds worldBounds)
        {
            var result = new Bounds(transform.InverseTransformPoint(worldBounds.center), Vector3.zero);
            for (var corner = 0; corner < 8; corner++)
            {
                var offset = Vector3.Scale(worldBounds.extents, new Vector3(
                    (corner & 1) == 0 ? -1f : 1f,
                    (corner & 2) == 0 ? -1f : 1f,
                    (corner & 4) == 0 ? -1f : 1f));
                result.Encapsulate(transform.InverseTransformPoint(worldBounds.center + offset));
            }
            return result;
        }

        private void SetDimensions(Bounds localBounds)
        {
            centre = localBounds.center;
            centrelineLength = ProjectedSize(localBounds.size, longitudinal);
            radialThickness = ProjectedSize(localBounds.size, inward);
            normalThickness = ProjectedSize(localBounds.size, planeNormal);
        }

        private static float ProjectedSize(Vector3 size, Vector3 axis)
        {
            return Mathf.Abs(axis.x) * size.x + Mathf.Abs(axis.y) * size.y + Mathf.Abs(axis.z) * size.z;
        }

        private void SetDimensionsFromVertices()
        {
            var minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            foreach (var vertex in originalVertices)
            {
                var projected = new Vector3(Vector3.Dot(vertex, longitudinal),
                    Vector3.Dot(vertex, inward), Vector3.Dot(vertex, planeNormal));
                minimum = Vector3.Min(minimum, projected);
                maximum = Vector3.Max(maximum, projected);
            }
            var midpoint = (minimum + maximum) * 0.5f;
            centre = longitudinal * midpoint.x + inward * midpoint.y + planeNormal * midpoint.z;
            centrelineLength = maximum.x - minimum.x;
            radialThickness = maximum.y - minimum.y;
            normalThickness = maximum.z - minimum.z;
        }

        private float BendRadians(float amount) => amount * maximumBendDegrees * Mathf.Deg2Rad;

        private Vector3 PointAt(float normalizedPosition, float amount)
        {
            var distance = (normalizedPosition - 0.5f) * centrelineLength;
            var totalAngle = BendRadians(amount);
            if (Mathf.Abs(totalAngle) < 0.00001f)
                return centre + longitudinal * distance;
            var radius = centrelineLength / totalAngle;
            var angle = distance / radius;
            // 2 * sin(angle / 2)^2 avoids cancellation near the initial straight pose.
            var halfSine = Mathf.Sin(angle * 0.5f);
            return centre + longitudinal * (radius * Mathf.Sin(angle)) +
                   inward * (2f * radius * halfSine * halfSine);
        }

        private void UpdateGeometry()
        {
            var totalAngle = BendRadians(progress);
            for (var index = 0; index < originalVertices.Length; index++)
            {
                var relative = originalVertices[index] - centre;
                var distance = Vector3.Dot(relative, longitudinal);
                var position = distance / centrelineLength + 0.5f;
                var crossSection = relative - longitudinal * distance;
                var rotation = Quaternion.AngleAxis(distance / centrelineLength * totalAngle * Mathf.Rad2Deg, planeNormal);
                deformedVertices[index] = PointAt(position, progress) + rotation * crossSection;
                if (originalNormals.Length == originalVertices.Length)
                    deformedNormals[index] = rotation * originalNormals[index];
                if (originalTangents.Length == originalVertices.Length)
                {
                    var original = originalTangents[index];
                    var tangent = rotation * new Vector3(original.x, original.y, original.z);
                    deformedTangents[index] = new Vector4(tangent.x, tangent.y, tangent.z, original.w);
                }
            }
            deformedMesh.vertices = deformedVertices;
            if (deformedNormals.Length == originalVertices.Length) deformedMesh.normals = deformedNormals;
            else deformedMesh.RecalculateNormals();
            if (deformedTangents.Length == originalVertices.Length) deformedMesh.tangents = deformedTangents;
            else deformedMesh.RecalculateTangents();
            deformedMesh.RecalculateBounds();
            UpdateColliderSegments(progress);
            UpdateAnchors();
        }

        private void UpdateColliderSegments(float amount)
        {
            for (var index = 0; index < segmentColliders.Length; index++)
            {
                var segment = segmentColliders[index];
                if (segment == null) continue;
                var first = PointAt(index / (float)segmentColliders.Length, amount);
                var second = PointAt((index + 1f) / segmentColliders.Length, amount);
                var chord = second - first;
                var tangent = chord.sqrMagnitude > 0.0000001f ? chord.normalized : longitudinal;
                var radial = Vector3.Cross(planeNormal, tangent).normalized;
                segment.transform.localPosition = (first + second) * 0.5f;
                // Collider X follows the strip, Y is radial thickness, Z is the ring plane normal.
                segment.transform.localRotation = Quaternion.LookRotation(planeNormal, radial);
                segment.transform.localScale = Vector3.one;
                segment.center = Vector3.zero;
                var segmentAngle = BendRadians(amount) / segmentColliders.Length;
                var sagitta = amount > 0.0001f
                    ? centrelineLength / BendRadians(amount) * (1f - Mathf.Cos(segmentAngle * 0.5f))
                    : 0f;
                segment.size = new Vector3(chord.magnitude + colliderPadding,
                    radialThickness + colliderPadding + sagitta * 2f,
                    normalThickness + colliderPadding);
            }
        }

        private void UpdateAnchors()
        {
            if (centrelineLength <= 0f) return;
            var offset = planeNormal * anchorNormalOffset;
            if (seamAnchor != null)
                seamAnchor.position = transform.TransformPoint(PointAt(0f, progress) + offset);
            for (var index = 0; index < markingPoints.Length; index++)
            {
                if (markingPoints[index] == null) continue;
                var position = index < markingPositions.Length ? markingPositions[index] : 0f;
                markingPoints[index].position = transform.TransformPoint(PointAt(Mathf.Clamp01(position), progress) + offset);
            }
        }

        private bool FailInitialization(string message)
        {
            Debug.LogError(message, this);
            return false;
        }

        private void OnDestroy()
        {
            if (initialized && sourceRenderer != null) sourceRenderer.enabled = sourceWasEnabled;
            if (originalCollider != null) originalCollider.enabled = originalColliderWasEnabled;
            foreach (var segment in segmentColliders)
                if (segment != null) segment.enabled = false;
            if (deformedVisual != null) Destroy(deformedVisual);
            if (deformedMesh != null) Destroy(deformedMesh);
        }
    }
}
