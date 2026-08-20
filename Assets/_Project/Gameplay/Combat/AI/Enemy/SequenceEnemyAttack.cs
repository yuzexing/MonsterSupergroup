using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class SequenceEnemyAttack : EnemyAttackMelee
	{
		public int currentAttackCount;

		public int consecutiveAttacks = 1;

		public Vector3 areaSideWarpDistance = new Vector3(0f, 0.5f, 0f);

		public override void AttackWarningEnter()
		{
			base.AttackWarningEnter();
			if (currentAttackCount <= consecutiveAttacks && currentAttackCount % 2 != 0)
			{
				_attack.transform.localPosition = areaSideWarpDistance;
			}
			else
			{
				_attack.transform.localPosition = -areaSideWarpDistance;
			}
		}

		public override void RecoveryEnter()
		{
			currentAttackCount++;
			if (currentAttackCount <= consecutiveAttacks)
			{
				base.controller.TransitionToWarning();
				return;
			}
			currentAttackCount = 0;
			base.RecoveryEnter();
		}

		public override void CancelAttack()
		{
			currentAttackCount = 0;
			base.CancelAttack();
		}
	}
}
