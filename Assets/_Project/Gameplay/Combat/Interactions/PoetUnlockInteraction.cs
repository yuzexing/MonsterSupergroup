using AstralShift.HellMaiden.Controllers;
using AstralShift.HellMaiden.Data;
using AstralShift.Managers;
using AstralShift.QTI.Interactions;
using AstralShift.QTI.Interactors;
// using PixelCrushers.DialogueSystem;
using UnityEngine;

namespace AstralShift.HellMaiden.Interactions
{
	public class PoetUnlockInteraction : Interaction
	{
		[SerializeField]
		private AnimatedSplashController poetUnlockPrefab;

		[SerializeField]
		private PoetPoolID poetPoolID;

		[SerializeField]
		private bool unlocksNewWeapon;

		// [SerializeField]
		// [VariablePopup(false)]
		// private string WeaponsUpdatedTrigger;

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			if (unlocksNewWeapon)
			{
				// GameDataManager.RegisterGameTrigger(WeaponsUpdatedTrigger, state: true);
			}
			GameDirector.Instance.runtimeDB.UnlockPoetPool(poetPoolID);
			AnimatedSplashController controllerInstance = Object.Instantiate(poetUnlockPrefab);
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
