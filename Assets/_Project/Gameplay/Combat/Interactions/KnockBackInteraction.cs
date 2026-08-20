using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.QTI.Interactions;
using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.HellMaiden.Interactions
{
	public class KnockBackInteraction : Interaction
	{
		[Header("Knockback Settings")]
		[SerializeField]
		private float knockbackRadius;

		[SerializeField]
		private LayerMask enemyLayers;

		[SerializeField]
		private KnockbackSettings knockbackSettings;

		[SerializeField]
		private bool alsoDamagePlayer = true;

		[SerializeField]
		private PlayerDamageInteraction damageInteraction;

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			PlayerHitbox component2;
			if (interactor.Transform.TryGetComponent<BaseEnemyController>(out var component))
			{
				component.BruteforceKnockBack(base.transform.position, knockbackSettings);
			}
			else if (interactor.Transform.TryGetComponent<PlayerHitbox>(out component2))
			{
				if (alsoDamagePlayer)
				{
					damageInteraction.DamagePlayer();
				}
				GameDirector.Instance.Player.BruteforceKnockBack(base.transform.position, knockbackSettings);
			}
			OnEnd();
		}

		public void KnockbackEverything()
		{
			RaycastHit2D[] array = Physics2D.CircleCastAll(base.transform.position, knockbackRadius, Vector2.up, 0f, enemyLayers);
			if (array.Length == 0)
			{
				return;
			}
			RaycastHit2D[] array2 = array;
			for (int i = 0; i < array2.Length; i++)
			{
				RaycastHit2D raycastHit2D = array2[i];
				PlayerHitbox component2;
				if (raycastHit2D.transform.TryGetComponent<BaseEnemyController>(out var component))
				{
					component.BruteforceKnockBack(base.transform.position, knockbackSettings);
				}
				else if (!GameDirector.Instance.Player.IsInvulnerable && raycastHit2D.collider.transform.TryGetComponent<PlayerHitbox>(out component2))
				{
					if (alsoDamagePlayer)
					{
						damageInteraction.DamagePlayer();
					}
					GameDirector.Instance.Player.BruteforceKnockBack(base.transform.position, knockbackSettings);
				}
			}
		}
	}
}
