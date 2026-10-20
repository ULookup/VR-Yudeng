using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Yudeng.Presentation
{
    /// <summary>Uses the host's Chinese font for the workshop panels and material labels.</summary>
    public sealed class WorkshopChineseFont : MonoBehaviour
    {
        [SerializeField] private Transform contentRoot;
        private Font runtimeFont;

        public void Configure(Transform root = null)
        {
            contentRoot = root;
            if (Application.isPlaying)
                ApplyFont();
        }

        private void Awake() => ApplyFont();

        private void ApplyFont()
        {
            if (runtimeFont == null)
                runtimeFont = Font.CreateDynamicFontFromOSFont(
                    new[] { "Microsoft YaHei", "SimHei", "Noto Sans CJK SC", "Arial" }, 48);
            if (runtimeFont == null)
                return;

            var roots = contentRoot != null
                ? new[] { contentRoot.gameObject }
                : gameObject.scene.GetRootGameObjects();
            var labels = new List<Text>();
            var signs = new List<TextMesh>();
            foreach (var root in roots)
            {
                labels.Clear();
                signs.Clear();
                root.GetComponentsInChildren(true, labels);
                root.GetComponentsInChildren(true, signs);
                foreach (var label in labels)
                    label.font = runtimeFont;
                foreach (var sign in signs)
                {
                    sign.font = runtimeFont;
                    var signRenderer = sign.GetComponent<MeshRenderer>();
                    if (signRenderer != null)
                        signRenderer.sharedMaterial = runtimeFont.material;
                }
            }
        }

        private void OnDestroy()
        {
            if (runtimeFont != null)
                Destroy(runtimeFont);
        }
    }
}
