using UnityEngine;

namespace FishLantern.XR
{
    /// <summary>Loads pigment only from a held, activated brush's physical working end.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider), typeof(InteractableIdentity), typeof(InteractionGate))]
    public sealed class PaintColorWell : MonoBehaviour
    {
        [SerializeField] private string colorId = "red";
        [SerializeField] private string colorLabel = "朱红";
        [SerializeField] private Color pigmentColor = new(0.65f, 0.08f, 0.04f, 1f);
        private InteractableIdentity identity;
        private InteractionGate gate;

        public string ColorId => colorId;
        public string ColorLabel => colorLabel;
        public Color PigmentColor => pigmentColor;

        public void Configure(string id, string label, Color color)
        {
            colorId = id;
            colorLabel = label;
            pigmentColor = color;
            GetComponent<Collider>().isTrigger = true;
        }

        private void Awake()
        {
            identity = GetComponent<InteractableIdentity>();
            gate = GetComponent<InteractionGate>();
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other) => TryDip(other);
        private void OnTriggerStay(Collider other) => TryDip(other);

        private void TryDip(Collider other)
        {
            if (!isActiveAndEnabled || other == null || other.GetComponent<ToolContactPoint>() == null) return;
            var tool = other.GetComponentInParent<PaperPaintTool>();
            if (tool == null || tool.IsPencil || !tool.IsDrawing ||
                !gate.CanPerform(InteractionKind.Activate, reportDenial: false)) return;
            if (!tool.TryLoadPaint(colorId, colorLabel, pigmentColor)) return;
            var toolIdentity = tool.GetComponent<InteractableIdentity>();
            InteractionServiceHub.Active?.Report(identity.CreateEvent(InteractionKind.Activate,
                InteractionResult.Succeeded, targetId: toolIdentity != null ? toolIdentity.StableId : string.Empty,
                details: colorLabel));
        }
    }
}
