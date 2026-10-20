using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

namespace FishLantern.XR
{
    /// <summary>
    /// Default scene feedback provider using renderer property blocks, one-shot audio and XR haptic players.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DefaultInteractionFeedbackService : MonoBehaviour, IInteractionFeedbackService, IInteractionResettable
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private InteractionFeedbackProfile profile;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private HapticImpulsePlayer leftHaptics;
        [SerializeField] private HapticImpulsePlayer rightHaptics;
        [SerializeField] private UnityEvent successAnimationRequested = new();
        [SerializeField] private UnityEvent errorAnimationRequested = new();

        private readonly Dictionary<InteractableIdentity, Coroutine> flashes = new();
        private readonly HashSet<InteractableIdentity> highlighted = new();
        private MaterialPropertyBlock propertyBlock;

        /// <summary>Assigns dependencies from scene bootstrap or tests.</summary>
        public void Configure(
            InteractionFeedbackProfile feedbackProfile,
            AudioSource source,
            HapticImpulsePlayer left,
            HapticImpulsePlayer right)
        {
            profile = feedbackProfile;
            audioSource = source;
            leftHaptics = left;
            rightHaptics = right;
        }

        /// <inheritdoc />
        public void SetHighlight(InteractableIdentity identity, bool enabled)
        {
            if (identity == null)
                return;

            if (enabled)
            {
                highlighted.Add(identity);
                ApplyColor(identity, ProfileHighlightColor);
            }
            else
            {
                highlighted.Remove(identity);
                if (!flashes.ContainsKey(identity))
                    ClearColor(identity);
            }
        }

        /// <inheritdoc />
        public void PlaySuccess(InteractableIdentity identity, InteractionKind interactionKind)
        {
            if (identity == null)
                return;

            Flash(identity, ProfileSuccessColor, ProfileSuccessDuration);
            PlayClip(profile != null ? profile.SuccessClip : null);
            Pulse(InteractionHand.Both, ProfileSuccessAmplitude, ProfileSuccessHapticDuration);
            successAnimationRequested.Invoke();
        }

        /// <inheritdoc />
        public void PlayError(InteractableIdentity identity, InteractionFailureReason failureReason)
        {
            if (identity == null)
                return;

            Flash(identity, ProfileErrorColor, ProfileErrorDuration);
            PlayClip(profile != null ? profile.ErrorClip : null);
            Pulse(InteractionHand.Both, ProfileErrorAmplitude, ProfileErrorHapticDuration);
            errorAnimationRequested.Invoke();
        }

        /// <inheritdoc />
        public void Pulse(InteractionHand hand, float amplitude, float durationSeconds)
        {
            amplitude = Mathf.Clamp01(amplitude);
            durationSeconds = Mathf.Max(0f, durationSeconds);
            if (durationSeconds <= 0f || amplitude <= 0f)
                return;

            if (hand is InteractionHand.Left or InteractionHand.Both or InteractionHand.Unknown)
                leftHaptics?.SendHapticImpulse(amplitude, durationSeconds);
            if (hand is InteractionHand.Right or InteractionHand.Both or InteractionHand.Unknown)
                rightHaptics?.SendHapticImpulse(amplitude, durationSeconds);
        }

        private void Awake()
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        private Color ProfileHighlightColor => profile != null
            ? profile.HighlightColor
            : new Color(0.15f, 0.9f, 1f, 1f);

        private Color ProfileSuccessColor => profile != null
            ? profile.SuccessColor
            : new Color(0.2f, 1f, 0.35f, 1f);

        private Color ProfileErrorColor => profile != null
            ? profile.ErrorColor
            : new Color(1f, 0.12f, 0.08f, 1f);

        private float ProfileSuccessDuration => profile != null ? profile.SuccessFlashSeconds : 0.2f;
        private float ProfileErrorDuration => profile != null ? profile.ErrorFlashSeconds : 0.35f;
        private float ProfileSuccessAmplitude => profile != null ? profile.SuccessAmplitude : 0.25f;
        private float ProfileSuccessHapticDuration => profile != null ? profile.SuccessDuration : 0.08f;
        private float ProfileErrorAmplitude => profile != null ? profile.ErrorAmplitude : 0.55f;
        private float ProfileErrorHapticDuration => profile != null ? profile.ErrorDuration : 0.16f;

        private void Flash(InteractableIdentity identity, Color color, float duration)
        {
            if (flashes.TryGetValue(identity, out var existing) && existing != null)
                StopCoroutine(existing);

            flashes[identity] = StartCoroutine(FlashRoutine(identity, color, duration));
        }

        private IEnumerator FlashRoutine(InteractableIdentity identity, Color color, float duration)
        {
            ApplyColor(identity, color);
            yield return new WaitForSecondsRealtime(Mathf.Max(0.01f, duration));
            flashes.Remove(identity);

            if (identity == null)
                yield break;

            if (highlighted.Contains(identity))
                ApplyColor(identity, ProfileHighlightColor);
            else
                ClearColor(identity);
        }

        private void ApplyColor(InteractableIdentity identity, Color color)
        {
            propertyBlock ??= new MaterialPropertyBlock();
            foreach (var targetRenderer in identity.GetComponentsInChildren<Renderer>(true))
            {
                targetRenderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetColor(BaseColorId, color);
                propertyBlock.SetColor(ColorId, color);
                propertyBlock.SetColor(EmissionColorId, color * 0.35f);
                targetRenderer.SetPropertyBlock(propertyBlock);
                propertyBlock.Clear();
            }
        }

        private void ClearColor(InteractableIdentity identity)
        {
            if (identity == null)
                return;

            foreach (var targetRenderer in identity.GetComponentsInChildren<Renderer>(true))
                targetRenderer.SetPropertyBlock(null);
        }

        private void PlayClip(AudioClip clip)
        {
            if (audioSource != null && clip != null)
                audioSource.PlayOneShot(clip, profile != null ? profile.AudioVolume : 0.8f);
        }

        public void ResetInteraction()
        {
            StopAllCoroutines();
            foreach (var identity in flashes.Keys)
                ClearColor(identity);
            foreach (var identity in highlighted)
                ClearColor(identity);
            highlighted.Clear();
            flashes.Clear();
            if (audioSource != null) audioSource.Stop();
        }

        private void OnDisable() => ResetInteraction();

        private void OnDestroy() => ResetInteraction();
    }
}
