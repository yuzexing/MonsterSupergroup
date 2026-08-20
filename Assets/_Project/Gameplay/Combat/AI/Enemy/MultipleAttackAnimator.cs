using System.Collections.Generic;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class MultipleAttackAnimator : EnemyAnimator
	{
		[Header("Attack Script")]
		[SerializeField]
		private SequenceEnemyAttack attack;

		[Header("Enemy Consecutive Attack Animation")]
		[SerializeField]
		private List<ClipQuad> attackSets = new List<ClipQuad>();

		public override float AttackTime => attackSets[attack.currentAttackCount].attackLeftUp.Length;

		public override float AttackWarningTime => attackSets[attack.currentAttackCount].attackWarningLeftUp.Length;

		public override void Attack(float x, float y)
		{
			int currentAttackCount = attack.currentAttackCount;
			if (x > 0f)
			{
				animancer.Layers[0].Play((y > 0f) ? attackSets[currentAttackCount].attackRightUp : attackSets[currentAttackCount].attackRightDown, 0f);
			}
			else
			{
				animancer.Layers[0].Play((y > 0f) ? attackSets[currentAttackCount].attackLeftUp : attackSets[currentAttackCount].attackLeftDown, 0f);
			}
		}

		public override void AttackWarning(float x, float y)
		{
			int currentAttackCount = attack.currentAttackCount;
			if (x > 0f)
			{
				animancer.Layers[0].Play((y > 0f) ? attackSets[currentAttackCount].attackWarningRightUp : attackSets[currentAttackCount].attackWarningRightDown, 0f);
			}
			else
			{
				animancer.Layers[0].Play((y > 0f) ? attackSets[currentAttackCount].attackWarningLeftUp : attackSets[currentAttackCount].attackWarningLeftDown, 0f);
			}
		}

		public override void Recovery(float x, float y)
		{
			if (attack.currentAttackCount == attackSets.Count - 1)
			{
				if (x > 0f)
				{
					animancer.Layers[0].Play((y > 0f) ? recoveryRightUp : recoveryRightDown, 0f);
				}
				else
				{
					animancer.Layers[0].Play((y > 0f) ? recoveryLeftUp : recoveryLeftDown, 0f);
				}
			}
		}
	}
}
