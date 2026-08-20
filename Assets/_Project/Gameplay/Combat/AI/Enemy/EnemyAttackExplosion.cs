using AstralShift.HellMaiden.Combat;
using AstralShift.Pooling;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class EnemyAttackExplosion : EnemyAttack
	{
		[SerializeField]
		private EnemyAttackWarning _attackWarningPrefab;

		[SerializeField]
		private EnemyExplosionAttackVFX _attackVFXPrefab;

		private GenericPooler<EnemyAttackWarning> _attackWarningPool;

		private GenericPooler<EnemyExplosionAttackVFX> _attackPool;

		private EnemyAttackWarning _attackWarning;

		private EnemyExplosionAttackVFX _attackVFX;

		[SerializeField]
		private Transform explosionPosition;

		[SerializeField]
		private float chaseSpeed = 6.2f;

		public override void AttackWarningEnter()
		{
			if (!(_attackWarning != null))
			{
				_warningStartTime = Time.time;
				base.controller.stats.Speed = chaseSpeed;
				SpawnWarningFromPool();
			}
		}

		public override void AttackWarningTick()
		{
			base.enemyAnimator.AttackWarning(base.controller.FacingDirection.x, base.controller.FacingDirection.y);
			base.AttackWarningTick();
		}

		public override void AttackWarningExit()
		{
			base.AttackWarningExit();
			ReturnWarningToPool();
		}

		public override void CancelAttack()
		{
			base.controller.Attack();
		}

		public override void AttackEnter()
		{
			base.AttackEnter();
			base.controller.ActivateColliders(activate: false);
			base.controller.Movement.FreezeRigidbody(state: true);
			_attackPool = PoolManager.Instance.GetOrCreatePooler(_attackVFXPrefab);
			_attackVFX = _attackPool.GetOrCreate(base.transform, activate: true);
			_attackVFX.transform.position = explosionPosition.position;
			_attackVFX.SetStats(base.controller.stats);
			base.controller.stats.Speed = base.controller.stats.BaseSpeed;
			_attackVFX.Trigger(null);
		}

		public override void RecoveryEnter()
		{
			base.RecoveryEnter();
			_attackVFX.Stop();
			onRecoveryEnd = DestroyEnemy;
		}

		private void DestroyEnemy()
		{
			base.controller.Kill(instant: true, dropXp: false);
			_attackPool.Return(_attackVFX);
		}

		private void SpawnWarningFromPool()
		{
			_attackWarningPool = PoolManager.Instance.GetOrCreatePooler(_attackWarningPrefab);
			_attackWarning = _attackWarningPool.GetOrCreate(base.transform, activate: true);
			_attackWarning.transform.position = base.transform.position;
			_attackWarning.SetWarningTime(base.WarningTime, base.AttackTime);
			_attackWarning.Show();
		}

		private void ReturnWarningToPool()
		{
			if (_attackWarning != null)
			{
				_attackWarning.Hide();
				_attackWarningPool.Return(_attackWarning);
				_attackWarning = null;
			}
		}
	}
}
