using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using Yudeng.Experience;

namespace Yudeng.Input
{
    /// <summary>Applies the existing workshop pause state to both XR locomotion inputs.</summary>
    [DefaultExecutionOrder(-29000)]
    public sealed class WorkshopLocomotionState : MonoBehaviour
    {
        [SerializeField] DesktopXrInputRig input;
        [SerializeField] ContinuousMoveProvider movement;
        [SerializeField] SnapTurnProvider turning;
        [SerializeField] FishLanternExperienceController experience;

        public void Configure(DesktopXrInputRig source, ContinuousMoveProvider move, SnapTurnProvider turn, FishLanternExperienceController workshop)
        {
            input = source;
            movement = move;
            turning = turn;
            experience = workshop;
        }

        void Update()
        {
            bool available = input != null && !input.InputPaused && !input.IsSimulationResetPending &&
                (experience == null || !experience.IsPaused);
            if (movement != null && movement.enabled != available) movement.enabled = available;
            if (turning != null && turning.enabled != available) turning.enabled = available;
        }
    }
}
