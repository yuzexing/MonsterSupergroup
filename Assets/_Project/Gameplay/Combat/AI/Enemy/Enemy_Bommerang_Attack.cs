using Animancer;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class Enemy_Bommerang_Attack : EnemyProjectileAttack
	{
		[SerializeField]
		private ClipTransition noBommerangWalkLeft;

		[SerializeField]
		private ClipTransition noBommerangWalkRight;

		[SerializeField]
		private ClipTransition noBommerangDeadLeft;

		[SerializeField]
		private ClipTransition noBommerangDeadRight;

		[SerializeField]
		private ClipTransition noBommerangHurtLeft;

		[SerializeField]
		private ClipTransition noBommerangHurtRight;

		private ClipTransition baseBomerangWalkLeft;

		private ClipTransition baseBomerangWalkRight;

		private ClipTransition baseBomerangDeadLeft;

		private ClipTransition baseBomerangDeadRight;

		private ClipTransition baseBomerangHurtLeft;

		private ClipTransition baseBomerangHurtRight;

		private void Start()
		{
			base.controller.StateMachine.AddAnyTransition(base.controller.Recovery);
			base.controller.StateMachine.AddTransition(base.controller.Warning, base.controller.Moving);
			baseBomerangWalkLeft = base.controller.enemyAnimator.MoveLeftUp;
			baseBomerangWalkRight = base.controller.enemyAnimator.MoveRightUp;
			baseBomerangDeadLeft = base.controller.enemyAnimator.DeadLeftUp;
			baseBomerangDeadRight = base.controller.enemyAnimator.DeadRightUp;
			baseBomerangHurtLeft = base.controller.enemyAnimator.HurtLeftUp;
			baseBomerangHurtRight = base.controller.enemyAnimator.HurtRightUp;
		}

		public override void AttackWarningEnter()
		{
			if ((bool)_currentBullet)
			{
				base.controller.TransitionToMoving();
				return;
			}
			base.AttackWarningEnter();
			SetNoBoomerangAnimations();
			base.controller.attackCooldown = _currentBullet.duration;
		}

		public override void RecoveryEnter()
		{
			if ((bool)_currentBullet)
			{
				onRecoveryEnd?.Invoke();
			}
			else
			{
				base.RecoveryEnter();
			}
		}

		protected override void ReturnAttack(BulletProjectile bullet)
		{
			base.ReturnAttack(bullet);
			base.controller.TransitionToRecovery();
			SetBoomerangAnimations();
		}

		private void SetNoBoomerangAnimations()
		{
			base.controller.enemyAnimator.MoveLeftUp = noBommerangWalkLeft;
			base.controller.enemyAnimator.MoveLeftDown = noBommerangWalkLeft;
			base.controller.enemyAnimator.MoveRightUp = noBommerangWalkRight;
			base.controller.enemyAnimator.MoveRightDown = noBommerangWalkRight;
			base.controller.enemyAnimator.DeadLeftUp = noBommerangDeadLeft;
			base.controller.enemyAnimator.DeadLeftDown = noBommerangDeadLeft;
			base.controller.enemyAnimator.DeadRightUp = noBommerangDeadRight;
			base.controller.enemyAnimator.DeadRightDown = noBommerangDeadRight;
			base.controller.enemyAnimator.HurtLeftUp = noBommerangHurtLeft;
			base.controller.enemyAnimator.HurtLeftDown = noBommerangHurtLeft;
			base.controller.enemyAnimator.HurtRightUp = noBommerangHurtRight;
			base.controller.enemyAnimator.HurtRightDown = noBommerangHurtRight;
		}

		private void SetBoomerangAnimations()
		{
			base.controller.enemyAnimator.MoveLeftUp = baseBomerangWalkLeft;
			base.controller.enemyAnimator.MoveLeftDown = baseBomerangWalkLeft;
			base.controller.enemyAnimator.MoveRightUp = baseBomerangWalkRight;
			base.controller.enemyAnimator.MoveRightDown = baseBomerangWalkRight;
			base.controller.enemyAnimator.DeadLeftUp = baseBomerangDeadLeft;
			base.controller.enemyAnimator.DeadLeftDown = baseBomerangDeadLeft;
			base.controller.enemyAnimator.DeadRightUp = baseBomerangDeadRight;
			base.controller.enemyAnimator.DeadRightDown = baseBomerangDeadRight;
			base.controller.enemyAnimator.HurtLeftUp = baseBomerangHurtLeft;
			base.controller.enemyAnimator.HurtLeftDown = baseBomerangHurtLeft;
			base.controller.enemyAnimator.HurtRightUp = baseBomerangHurtRight;
			base.controller.enemyAnimator.HurtRightDown = baseBomerangHurtRight;
		}
	}
}
