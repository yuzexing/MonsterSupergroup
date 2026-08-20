using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.QTI.Interactions;
using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.HellMaiden.Interactions
{
	public class SetImpulseInteraction : Interaction
	{
		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			GameDirector.Instance.Player.DebuffForce = Vector2.zero;
			GameDirector.Instance.Player.WindForce = Vector2.zero;
			OnEnd();
		}

		private void OnTriggerExit2D(Collider2D other)
		{
			EnemyController componentInParent = other.GetComponentInParent<EnemyController>();
			if ((bool)componentInParent && componentInParent.enemyFlyingType)
			{
				componentInParent.windDirection = Vector2.zero;
			}
		}
	}
}
