using AstralShift.HellMaiden.Interactions;
using AstralShift.QTI.Triggers;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class EnemyAttackSuicide : EnemyAttack
	{
		public PlayerDamageInteraction damageInteraction;

		private void Start()
		{
			if (!(damageInteraction == null))
			{
				damageInteraction.enemyStats = base.controller.stats;
			}
		}

		private void OnEnable()
		{
			damageInteraction.GetComponent<InteractionTrigger>().enabled = true;
		}

		public override void CancelAttack()
		{
			damageInteraction.enemyStats = base.controller.stats;
			damageInteraction.GetComponent<InteractionTrigger>().enabled = true;
		}

		public void KillEnemy()
		{
			if ((bool)base.controller)
			{
				base.controller.Kill(instant: false, dropXp: false);
			}
			damageInteraction.GetComponent<InteractionTrigger>().enabled = false;
		}
	}
}
