using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player;
using AstralShift.QTI.Interactions;
using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.HellMaiden.Interactions
{
	public class PushPullInteraction : Interaction
	{
		[SerializeField]
		private bool affectGroundTroops;

		[SerializeField]
		private bool push;

		[SerializeField]
		private bool affectPlayer;

		[SerializeField]
		private float strength = 5f;

		private PlayerMovement player;

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			player = GameDirector.Instance.Player;
			OnEnd();
		}

		private void OnTriggerStay2D(Collider2D other)
		{
			if (!affectPlayer)
			{
				EnemyController componentInParent = other.GetComponentInParent<EnemyController>();
				if ((bool)componentInParent)
				{
					if (affectGroundTroops)
					{
						componentInParent.forceWindInteraction = true;
					}
					if (push)
					{
						componentInParent.windDirection += ((Vector2)base.transform.position - (Vector2)componentInParent.transform.position).normalized * strength;
					}
					else
					{
						componentInParent.windDirection += ((Vector2)componentInParent.transform.position - (Vector2)base.transform.position).normalized * strength;
					}
				}
			}
			else if ((bool)player)
			{
				if (push)
				{
					Vector2 vector = (Vector2)base.transform.position - (Vector2)player.transform.position;
					player.WindForce = vector.normalized * strength;
				}
				else
				{
					Vector2 vector2 = (Vector2)player.transform.position - (Vector2)base.transform.position;
					player.WindForce = vector2.normalized * strength;
				}
			}
		}

		private void OnTriggerExit2D(Collider2D other)
		{
			if (!affectPlayer)
			{
				EnemyController componentInParent = other.GetComponentInParent<EnemyController>();
				if ((bool)componentInParent)
				{
					if (!componentInParent.enemyFlyingType)
					{
						componentInParent.forceWindInteraction = false;
					}
					componentInParent.windDirection = Vector2.zero;
				}
			}
			else
			{
				player.WindForce = Vector2.zero;
			}
		}
	}
}
