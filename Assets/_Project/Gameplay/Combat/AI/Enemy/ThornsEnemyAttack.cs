using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Interactions;
using AstralShift.QTI.Triggers;
using UnityEngine;

public class ThornsEnemyAttack : EnemyAttack
{
	public PlayerDamageInteraction damageInteraction;

	[SerializeField]
	private bool deactivateColliderOnCancelAttack = true;

	public override void CancelAttack()
	{
		damageInteraction.enemyStats = base.controller.stats;
		damageInteraction.GetComponent<InteractionTrigger>().enabled = true;
		damageInteraction.gameObject.SetActive(!deactivateColliderOnCancelAttack);
	}

	private void OnEnable()
	{
		damageInteraction.gameObject.SetActive(value: true);
	}

	public void KillEnemy()
	{
		if ((bool)base.controller)
		{
			base.controller.Kill(instant: false, dropXp: false);
		}
		damageInteraction.gameObject.SetActive(value: false);
	}

	private void Start()
	{
		if (!(damageInteraction == null))
		{
			damageInteraction.enemyStats = base.controller.stats;
		}
	}
}
