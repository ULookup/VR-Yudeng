using UnityEngine;

namespace FishLantern.XR
{
    /// <summary>Centralized visual, audio and haptic tuning for fish-lantern interactions.</summary>
    [CreateAssetMenu(
        fileName = "InteractionFeedbackProfile",
        menuName = "Fish Lantern XR/Interaction Feedback Profile")]
    public sealed class InteractionFeedbackProfile : ScriptableObject
    {
        [Header("Visual")]
        [SerializeField, ColorUsage(true, true)] private Color highlightColor = new(0.15f, 0.9f, 1f, 1f);
        [SerializeField, ColorUsage(true, true)] private Color successColor = new(0.2f, 1f, 0.35f, 1f);
        [SerializeField, ColorUsage(true, true)] private Color errorColor = new(1f, 0.12f, 0.08f, 1f);
        [SerializeField, Min(0.01f)] private float successFlashSeconds = 0.2f;
        [SerializeField, Min(0.01f)] private float errorFlashSeconds = 0.35f;

        [Header("Audio")]
        [SerializeField] private AudioClip successClip;
        [SerializeField] private AudioClip errorClip;
        [SerializeField, Range(0f, 1f)] private float audioVolume = 0.8f;

        [Header("Haptics")]
        [SerializeField, Range(0f, 1f)] private float successAmplitude = 0.25f;
        [SerializeField, Min(0f)] private float successDuration = 0.08f;
        [SerializeField, Range(0f, 1f)] private float errorAmplitude = 0.55f;
        [SerializeField, Min(0f)] private float errorDuration = 0.16f;

        /// <summary>Persistent eligible-object highlight color.</summary>
        public Color HighlightColor => highlightColor;

        /// <summary>Positive completion flash color.</summary>
        public Color SuccessColor => successColor;

        /// <summary>Denied or invalid interaction flash color.</summary>
        public Color ErrorColor => errorColor;

        /// <summary>Positive flash duration in seconds.</summary>
        public float SuccessFlashSeconds => successFlashSeconds;

        /// <summary>Error flash duration in seconds.</summary>
        public float ErrorFlashSeconds => errorFlashSeconds;

        /// <summary>Optional success sound.</summary>
        public AudioClip SuccessClip => successClip;

        /// <summary>Optional error sound.</summary>
        public AudioClip ErrorClip => errorClip;

        /// <summary>Volume used for one-shot feedback audio.</summary>
        public float AudioVolume => audioVolume;

        /// <summary>Success haptic amplitude.</summary>
        public float SuccessAmplitude => successAmplitude;

        /// <summary>Success haptic duration in seconds.</summary>
        public float SuccessDuration => successDuration;

        /// <summary>Error haptic amplitude.</summary>
        public float ErrorAmplitude => errorAmplitude;

        /// <summary>Error haptic duration in seconds.</summary>
        public float ErrorDuration => errorDuration;
    }
}
