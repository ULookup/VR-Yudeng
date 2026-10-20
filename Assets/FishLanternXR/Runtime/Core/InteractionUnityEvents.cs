using System;
using UnityEngine.Events;

namespace FishLantern.XR
{
    /// <summary>Inspector-serializable event carrying normalized interaction data.</summary>
    [Serializable]
    public sealed class InteractionEventUnityEvent : UnityEvent<InteractionEventData>
    {
    }

    /// <summary>Inspector-serializable event carrying the authoritative step.</summary>
    [Serializable]
    public sealed class FishLanternStepUnityEvent : UnityEvent<FishLanternStep>
    {
    }

    /// <summary>Inspector-serializable event carrying tutorial prompt data.</summary>
    [Serializable]
    public sealed class TutorialPromptUnityEvent : UnityEvent<TutorialPromptRequest>
    {
    }
}
