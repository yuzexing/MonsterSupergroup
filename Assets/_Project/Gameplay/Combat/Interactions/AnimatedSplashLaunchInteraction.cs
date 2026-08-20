using AstralShift.HellMaiden.Controllers;
using AstralShift.Managers;
using AstralShift.QTI.Interactions;
using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.HellMaiden.Interactions
{
	public class AnimatedSplashLaunchInteraction : Interaction
	{
		[SerializeField]
		private AnimatedSplashController splashPrefab;

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			AnimatedSplashController controllerInstance = Object.Instantiate(splashPrefab);
			ControllerManager.Instance.Subscribe(controllerInstance);
			ControllerManager.Instance.OverrideGameController<AnimatedSplashController>();
			controllerInstance.OnEnd += OnEndCallback;
			void OnEndCallback()
			{
				OnEnd();
				Object.Destroy(controllerInstance.gameObject);
			}
		}
	}
}
