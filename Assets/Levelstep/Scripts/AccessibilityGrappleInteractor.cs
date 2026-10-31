using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Levelstep
{
    /// <summary>
    /// Controlled selection helper, always colocated with the tracked physical controller.
    /// It only selects at normal grab distance; there is no remote hand offset or model.
    /// ClimbInteractable and ClimbProvider remain responsible for all climb locomotion.
    /// </summary>
    public class AccessibilityGrappleInteractor : XRBaseInteractor, IClimbHandInteraction
    {
        public AccessibilityGrappleController controller;
        public ClimbStaminaController.HandSide HandSide => controller != null ? controller.handSide : ClimbStaminaController.HandSide.Left;
        public bool ConsumesStamina => controller == null || controller.grapplingConsumesStamina;
        public override bool isSelectActive => controller != null && controller.WantsSelection;
        public override bool CanHover(IXRHoverInteractable interactable) => false;
        public override bool CanSelect(IXRSelectInteractable interactable) =>
            controller != null && ReferenceEquals(controller.SelectionTarget, interactable) && base.CanSelect(interactable);

        public override void GetValidTargets(List<IXRInteractable> targets)
        {
            targets.Clear();
            if (controller != null && controller.WantsSelection && controller.SelectionTarget != null)
                targets.Add(controller.SelectionTarget);
        }

        public override void PreprocessInteractor(XRInteractionUpdateOrder.UpdatePhase updatePhase)
        {
            base.PreprocessInteractor(updatePhase);
            if (controller != null) controller.SyncProxyPose();
        }

        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args);
            // Do not start another selection or move the proxy inside this event. The hold still
            // needs to finish its own exit handler and remove this grab from ClimbProvider.
            if (controller != null) controller.NotifySelectionExited();
        }
    }
}
