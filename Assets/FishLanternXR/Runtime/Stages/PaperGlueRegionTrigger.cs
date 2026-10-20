using UnityEngine;

namespace FishLantern.XR
{
    /// <summary>Connects one fitted surface region to contact coverage and its visible wet-glue layer.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class PaperGlueRegionTrigger : MonoBehaviour
    {
        [SerializeField] private ToolContactProgressInteraction owner;
        [SerializeField, Min(0)] private int regionIndex;
        [SerializeField] private Renderer wetGlueVisual;
        [SerializeField] private Color wetGlueColor = new(1f, 0.94f, 0.68f, 0.8f);

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");
        private MaterialPropertyBlock properties;
        private float displayedProgress = -1f;

        /// <summary>The optional visual must be a separate glue overlay, not the paper or skeleton renderer.</summary>
        public void Configure(ToolContactProgressInteraction coverageOwner, int index, Renderer glueVisual = null)
        {
            owner = coverageOwner;
            regionIndex = Mathf.Max(0, index);
            wetGlueVisual = glueVisual;
            GetComponent<Collider>().isTrigger = true;
            displayedProgress = -1f;
            ApplyCoverage();
        }

        public void ConfigureColor(Color appliedGlueColor)
        {
            wetGlueColor = appliedGlueColor;
            displayedProgress = -1f;
            ApplyCoverage();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (owner != null) owner.TrackRegionContact(regionIndex, other, true);
        }

        private void OnTriggerStay(Collider other)
        {
            if (owner != null) owner.TrackRegionContact(regionIndex, other);
        }

        private void OnTriggerExit(Collider other)
        {
            if (owner != null) owner.RemoveRegionContact(regionIndex, other);
        }

        private void OnEnable()
        {
            displayedProgress = -1f;
            ApplyCoverage();
        }

        private void LateUpdate() => ApplyCoverage();

        private void OnDisable()
        {
            if (owner != null) owner.ClearRegionContacts(regionIndex);
        }

        private void ApplyCoverage()
        {
            if (wetGlueVisual == null) return;
            var progress = owner != null ? owner.GetRegionProgress(regionIndex) : 0f;
            if (Mathf.Approximately(progress, displayedProgress)) return;
            displayedProgress = progress;
            wetGlueVisual.enabled = progress > 0f;
            properties ??= new MaterialPropertyBlock();
            wetGlueVisual.GetPropertyBlock(properties);
            var color = wetGlueColor;
            color.a *= Mathf.Clamp01(progress);
            properties.SetColor(BaseColor, color);
            properties.SetColor(ColorProperty, color);
            wetGlueVisual.SetPropertyBlock(properties);
        }

        private void Reset() => GetComponent<Collider>().isTrigger = true;
    }
}
