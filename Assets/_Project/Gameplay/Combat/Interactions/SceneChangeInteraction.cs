using AstralShift.HellMaiden.Scenes;
using AstralShift.QTI.Interactions;
using AstralShift.QTI.Interactors;
using AstralShift.QTI.Triggers;
using UnityEngine;

namespace AstralShift.HellMaiden.Interactions
{
	public class SceneChangeInteraction : Interaction
	{
		public SceneEnum scene;

		[SerializeField]
		private bool pauseDuringSceneChange = true;

		[SerializeField]
		private bool teleportAnimation;

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			OnEnd();
			SceneMaster.Instance.LoadScene(scene, unloadPreviousScene: true, pauseDuringSceneChange);
		}

		public override void Interact(IInteractor interactor, InteractionTrigger.TriggerActivation triggerActivation)
		{
			base.Interact(interactor, triggerActivation);
			if (teleportAnimation)
			{
				GameDirector.Instance.Player.TeleportAnimation();
			}
			OnEnd();
			SceneMaster.Instance.LoadScene(scene, unloadPreviousScene: true, pauseDuringSceneChange);
		}

		public override bool CanInteract()
		{
			return true;
		}
	}
}
