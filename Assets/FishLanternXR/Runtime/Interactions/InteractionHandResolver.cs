using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace FishLantern.XR
{
    /// <summary>Maps XR Interaction Toolkit handedness to the project contract.</summary>
    public static class InteractionHandResolver
    {
        /// <summary>Resolves the logical hand for an XR interactor.</summary>
        public static InteractionHand Resolve(IXRInteractor interactor)
        {
            if (interactor == null)
                return InteractionHand.Unknown;

            return interactor.handedness switch
            {
                InteractorHandedness.Left => InteractionHand.Left,
                InteractorHandedness.Right => InteractionHand.Right,
                _ => InteractionHand.Unknown,
            };
        }
    }
}
