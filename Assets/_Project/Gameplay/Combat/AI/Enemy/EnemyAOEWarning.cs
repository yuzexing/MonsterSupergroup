using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class EnemyAOEWarning : EnemyAttackWarning
	{
		public Animator animator;

		public override void Show()
		{
			animator.Play("In");
		}

		public override void Hide()
		{
			animator.Rebind();
			animator.Play("Idle");
		}

		public override UniTask AwaitableHide()
		{
			animator.Rebind();
			animator.Play("Idle");
			return UniTask.CompletedTask;
		}

		public override void SetWarningTime(float warningTime, float attackTime)
		{
		}
	}
}
