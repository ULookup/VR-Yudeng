using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishLantern.XR
{
    /// <summary>Lights the actual made paper and strokes after the two separate lamp actions.</summary>
    [DisallowMultipleComponent]
    public sealed class LampDisplayPresenter : MonoBehaviour, IInteractionResettable
    {
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private MonoBehaviour eventSource;
        [SerializeField] private MonoBehaviour stepSource;
        [SerializeField] private GameObject unfinishedModel;
        [SerializeField] private GameObject finishedModel;
        [SerializeField] private Light internalLight;
        [SerializeField] private Light[] additionalLights = Array.Empty<Light>();
        [SerializeField] private Renderer[] emissiveRenderers = Array.Empty<Renderer>();
        [SerializeField] private Renderer[] paintedRenderers = Array.Empty<Renderer>();
        [SerializeField] private GameObject[] installedObjects = Array.Empty<GameObject>();
        [SerializeField] private InteractableIdentity installAction;
        [SerializeField] private InteractableIdentity switchAction;
        [SerializeField, ColorUsage(false, true)] private Color emissionColor = new(1f, 0.76f, 0.42f);
        [SerializeField, Range(0f, 1.5f)] private float paperEmissionStrength = 0.55f;
        [SerializeField, Range(0f, 1f)] private float paintEmissionStrength = 0.18f;

        private readonly Dictionary<Renderer, Material[]> originalMaterials = new();
        private readonly List<Material> displayMaterials = new();
        private MaterialPropertyBlock block;
        private IInteractionEventStream stream;
        private IInteractionStepProvider provider;

        public bool IsInstalled { get; private set; }
        public bool IsLightOn { get; private set; }
        public event Action StateChanged;

        /// <summary>Retains the existing scene-builder entry point.</summary>
        public void Configure(MonoBehaviour events, MonoBehaviour steps, GameObject unfinished,
            GameObject finished, Light lampLight, Renderer[] glowRenderers)
        {
            Unsubscribe();
            eventSource = events;
            stepSource = steps;
            unfinishedModel = unfinished;
            finishedModel = finished;
            internalLight = lampLight;
            emissiveRenderers = glowRenderers ?? Array.Empty<Renderer>();
            ResetInteraction();
            if (isActiveAndEnabled) Subscribe();
        }

        public void ConfigureLighting(Light[] lights, Renderer[] paper, Renderer[] actualPainting,
            Color warmColor, float paperStrength = 0.55f, float paintStrength = 0.18f)
        {
            RestoreMaterials();
            additionalLights = lights ?? Array.Empty<Light>();
            emissiveRenderers = paper ?? Array.Empty<Renderer>();
            paintedRenderers = actualPainting ?? Array.Empty<Renderer>();
            emissionColor = warmColor;
            paperEmissionStrength = Mathf.Clamp(paperStrength, 0f, 1.5f);
            paintEmissionStrength = Mathf.Clamp01(paintStrength);
            SetLight(IsLightOn);
        }

        public void ConfigureActions(InteractableIdentity installation, InteractableIdentity lightSwitch)
        {
            installAction = installation;
            switchAction = lightSwitch;
        }

        public void ConfigureInstalledObjects(GameObject[] objects)
        {
            installedObjects = objects ?? Array.Empty<GameObject>();
            ApplyInstalledObjects();
        }

        public void ResetInteraction()
        {
            IsInstalled = false;
            IsLightOn = false;
            if (unfinishedModel != null) unfinishedModel.SetActive(true);
            if (finishedModel != null) finishedModel.SetActive(false);
            ApplyInstalledObjects();
            SetLight(false);
            RestoreMaterials();
            StateChanged?.Invoke();
        }

        private void OnEnable() => Subscribe();
        private void OnDisable() => Unsubscribe();
        private void OnDestroy() => RestoreMaterials();

        private void Subscribe()
        {
            Unsubscribe();
            stream = eventSource as IInteractionEventStream;
            provider = stepSource as IInteractionStepProvider;
            stream?.InteractionReported.AddListener(HandleEvent);
            provider?.StepChanged.AddListener(HandleStepChanged);
        }

        private void Unsubscribe()
        {
            stream?.InteractionReported.RemoveListener(HandleEvent);
            provider?.StepChanged.RemoveListener(HandleStepChanged);
            stream = null;
            provider = null;
        }

        private void HandleEvent(InteractionEventData data)
        {
            if (data == null || data.Result != InteractionResult.Succeeded ||
                data.Step != FishLanternStep.LampDisplay || provider?.CurrentStep != FishLanternStep.LampDisplay)
                return;
            if (data.InteractionKind == InteractionKind.LampInstalled &&
                (installAction == null || data.ObjectId == installAction.StableId))
            {
                IsInstalled = true;
                if (unfinishedModel != null) unfinishedModel.SetActive(false);
                if (finishedModel != null) finishedModel.SetActive(true);
                ApplyInstalledObjects();
                StateChanged?.Invoke();
            }
            else if (data.InteractionKind == InteractionKind.LightOn && IsInstalled &&
                (switchAction == null || data.ObjectId == switchAction.StableId))
            {
                IsLightOn = true;
                SetLight(true);
                StateChanged?.Invoke();
            }
        }

        private void HandleStepChanged(FishLanternStep step)
        {
            if (step is FishLanternStep.None or FishLanternStep.Welcome or FishLanternStep.LampDisplay)
                ResetInteraction();
        }

        private void ApplyInstalledObjects()
        {
            foreach (var item in installedObjects)
                if (item != null) item.SetActive(IsInstalled);
        }

        private void SetLight(bool enabled)
        {
            if (internalLight != null) internalLight.enabled = enabled;
            foreach (var light in additionalLights)
                if (light != null) light.enabled = enabled;
            ApplyEmission(emissiveRenderers, enabled ? paperEmissionStrength : 0f);
            ApplyEmission(paintedRenderers, enabled ? paintEmissionStrength : 0f);
        }

        private void ApplyEmission(Renderer[] renderers, float strength)
        {
            block ??= new MaterialPropertyBlock();
            foreach (var target in renderers)
            {
                if (target == null) continue;
                target.GetPropertyBlock(block);
                if (strength > 0f && Application.isPlaying)
                {
                    EnsureDisplayMaterials(target);
                    // PaperPaintSurface owns this map. Read it without replacing its texture or other properties.
                    var texture = block.GetTexture(BaseMapId);
                    if (texture == null && target.sharedMaterial != null && target.sharedMaterial.HasProperty(BaseMapId))
                        texture = target.sharedMaterial.GetTexture(BaseMapId);
                    if (texture != null) block.SetTexture(EmissionMapId, texture);
                }
                block.SetColor(EmissionColorId, emissionColor * strength);
                target.SetPropertyBlock(block);
                block.Clear();
            }
        }

        private void EnsureDisplayMaterials(Renderer target)
        {
            if (originalMaterials.ContainsKey(target)) return;
            var originals = target.sharedMaterials;
            var copies = new Material[originals.Length];
            for (var index = 0; index < originals.Length; index++)
            {
                var source = originals[index];
                if (source == null) continue;
                var copy = new Material(source) { name = source.name + "（本轮点灯）" };
                copy.EnableKeyword("_EMISSION");
                if (source.HasProperty(BaseMapId) && copy.HasProperty(EmissionMapId))
                    copy.SetTexture(EmissionMapId, source.GetTexture(BaseMapId));
                copies[index] = copy;
                displayMaterials.Add(copy);
            }
            originalMaterials[target] = originals;
            target.sharedMaterials = copies;
        }

        private void RestoreMaterials()
        {
            foreach (var pair in originalMaterials)
                if (pair.Key != null) pair.Key.sharedMaterials = pair.Value;
            originalMaterials.Clear();
            foreach (var material in displayMaterials)
                if (material != null)
                {
                    if (Application.isPlaying) Destroy(material);
                    else DestroyImmediate(material);
                }
            displayMaterials.Clear();
        }
    }
}
