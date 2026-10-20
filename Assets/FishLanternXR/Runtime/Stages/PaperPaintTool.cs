using System;
using UnityEngine;

namespace FishLantern.XR
{
    /// <summary>Drawing state on the existing held tool; raw input remains owned by XRI.</summary>
    [DisallowMultipleComponent]
    public sealed class PaperPaintTool : MonoBehaviour, IInteractionResettable
    {
        [SerializeField] private GrabInteractableEx grab;
        [SerializeField] private ToolContactPoint workingEnd;
        [SerializeField] private bool isPencil;
        [SerializeField] private Renderer tipRenderer;
        [SerializeField, Min(0.001f)] private float radiusMetres = 0.006f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorIdProperty = Shader.PropertyToID("_Color");
        private MaterialPropertyBlock tipProperties;
        private Color initialTipColor = Color.white;
        private bool capturedTipColor;
        private Color paintColor;
        private string colorId = string.Empty;
        private string colorLabel = string.Empty;
        private bool applicationPaused;

        public event Action ColorChanged;
        public bool IsPencil => isPencil;
        public bool HasPaint => !string.IsNullOrEmpty(colorId);
        public string ColorId => colorId;
        public string ColorLabel => HasPaint ? colorLabel : "尚未蘸色";
        public Color PaintColor => paintColor;
        public float RadiusMetres => radiusMetres;
        public Vector3 WorkingPosition => workingEnd != null ? workingEnd.transform.position : transform.position;
        public bool IsDrawing => isActiveAndEnabled && workingEnd != null &&
            workingEnd.isActiveAndEnabled && workingEnd.gameObject.activeInHierarchy &&
            grab != null && grab.IsHeldByHand && grab.TriggerPressed &&
            !applicationPaused && Application.isFocused;

        public void Configure(GrabInteractableEx heldTool, ToolContactPoint tip, bool pencil,
            Renderer coloredTip, float brushRadiusMetres = 0.006f)
        {
            grab = heldTool;
            workingEnd = tip;
            isPencil = pencil;
            tipRenderer = coloredTip;
            radiusMetres = Mathf.Max(0.001f, brushRadiusMetres);
            capturedTipColor = false;
            CaptureTipColor();
        }

        internal bool TryLoadPaint(string id, string label, Color color)
        {
            if (isPencil || !IsDrawing || (id != "red" && id != "green" && id != "black")) return false;
            if (colorId == id && paintColor == color) return false;
            colorId = id;
            colorLabel = label;
            paintColor = color;
            paintColor.a = 1f;
            ApplyTipColor(paintColor);
            ColorChanged?.Invoke();
            return true;
        }

        public void ResetInteraction()
        {
            colorId = string.Empty;
            colorLabel = string.Empty;
            paintColor = Color.clear;
            CaptureTipColor();
            ApplyTipColor(initialTipColor);
            ColorChanged?.Invoke();
        }

        private void Awake()
        {
            if (grab == null) grab = GetComponent<GrabInteractableEx>();
            CaptureTipColor();
            ResetInteraction();
        }

        private void OnApplicationPause(bool paused) => applicationPaused = paused;

        private void CaptureTipColor()
        {
            if (capturedTipColor || tipRenderer == null || tipRenderer.sharedMaterial == null) return;
            var material = tipRenderer.sharedMaterial;
            initialTipColor = material.HasProperty(BaseColorId) ? material.GetColor(BaseColorId) :
                material.HasProperty(ColorIdProperty) ? material.GetColor(ColorIdProperty) : Color.white;
            tipProperties ??= new MaterialPropertyBlock();
            tipRenderer.GetPropertyBlock(tipProperties);
            if (tipProperties.HasColor(BaseColorId)) initialTipColor = tipProperties.GetColor(BaseColorId);
            else if (tipProperties.HasColor(ColorIdProperty)) initialTipColor = tipProperties.GetColor(ColorIdProperty);
            capturedTipColor = true;
        }

        private void ApplyTipColor(Color color)
        {
            if (tipRenderer == null) return;
            tipProperties ??= new MaterialPropertyBlock();
            tipRenderer.GetPropertyBlock(tipProperties);
            tipProperties.SetColor(BaseColorId, color);
            tipProperties.SetColor(ColorIdProperty, color);
            tipRenderer.SetPropertyBlock(tipProperties);
        }
    }
}
