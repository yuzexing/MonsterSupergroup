using System;
using System.Collections;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Player;
using AstralShift.Pooling;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class FairyAttack : EnemyAttackDash
	{
		public EnemyAttackWarning bombAttack;

		[SerializeField]
		private EnemyExplosionAttackVFX _attackVFXPrefab;

		[SerializeField]
		private float dashDuration = 1.3f;

		protected GenericPooler<EnemyAttackWarning> bombPooler;

		private GenericPooler<EnemyExplosionAttackVFX> _attackPool;

		private EnemyExplosionAttackVFX _attackVFX;

		protected EnemyAttackWarning _attackBomb;

		public bool useBullet;

		public Transform bulletPosition;

		private BulletProjectile _currentBullet;

		public BulletProjectile bulletPrefab;

		protected GenericPooler<BulletProjectile> bulletPooler;

		private bool releaseBullet = true;

		private bool canDash;

		private CircularMovementBehavior circularMovementBehavior;

		private PlayerMovement player;

		[SerializeField]
		private float dashMultiplier = 2.3f;

		public override bool OverrideKnockback => true;

		private void Start()
		{
			base.controller.StateMachine.AddTransition(base.controller.Knockback, base.controller.Attacking);
			base.controller.StateMachine.AddTransition(base.controller.Knockback, base.controller.Warning);
			base.controller.StateMachine.AddTransition(base.controller.Knockback, base.controller.Recovery);
			if (damageInteraction != null)
			{
				damageInteraction.enemyStats = base.controller.stats;
			}
			collider = base.controller.collider;
			defaultLayerMask = collider.excludeLayers;
			circularMovementBehavior = base.controller.defaultMovement as CircularMovementBehavior;
			player = GameDirector.Instance.Player;
		}

		public override void AttackWarningEnter()
		{
			releaseBullet = true;
			if (useBullet)
			{
				bulletPooler = PoolManager.Instance.GetOrCreatePooler(bulletPrefab);
				BulletProjectile bulletProjectile = bulletPooler.GetOrCreate(bulletPosition);
				bulletProjectile.OnReturn = delegate
				{
					ReturnAttack(bulletProjectile);
				};
				bulletProjectile.transform.localPosition = Vector3.zero;
				bulletProjectile.fired = false;
				bulletProjectile.SetStats(base.controller.stats);
				bulletProjectile.gameObject.SetActive(value: true);
				_currentBullet = bulletProjectile;
			}
			base.AttackWarningEnter();
			_direction = (Vector2)base.Target.position - startPoint;
			_direction.Normalize();
			_direction *= Vector2.Distance(startPoint, player.transform.position) * dashMultiplier;
			endPoint = startPoint + _direction;
		}

		public override void AttackWarningExit()
		{
			base.AttackWarningExit();
			canDash = true;
		}

		public override void AttackTick()
		{
			if (Time.time - _attackStartTime > base.AttackTime)
			{
				onAttackEnd?.Invoke();
			}
		}

		private void Update()
		{
			if (!canDash)
			{
				return;
			}
			float num = Time.time - _attackStartTime;
			if (num > dashDuration / 2f && releaseBullet)
			{
				releaseBullet = false;
				if (useBullet)
				{
					if (_currentBullet == null)
					{
						return;
					}
					_currentBullet.transform.parent = null;
					_currentBullet.Fire(Vector2.zero);
				}
				else
				{
					bombPooler = PoolManager.Instance.GetOrCreatePooler(bombAttack);
					_attackBomb = bombPooler.GetOrCreate(null, activate: true);
					_attackBomb.transform.position = base.transform.position;
					_attackBomb.gameObject.SetActive(value: true);
					_attackBomb.SetWarningTime(base.WarningTime, base.AttackTime);
					_attackBomb.Show();
					StartCoroutine(DelayedAttackCoroutine(base.WarningTime));
				}
			}
			if (num < dashDuration)
			{
				float time = num / dashDuration;
				Vector2 vector;
				if (_returning && returnToInitialDashPosition)
				{
					float t = dashBacktCurve.Evaluate(time);
					vector = Vector2.Lerp(endPoint, startPoint, t);
				}
				else
				{
					float t = movementCurve.Evaluate(time);
					vector = Vector2.Lerp(startPoint, endPoint, t);
				}
				Vector2 linearVelocity = (vector - lastPosition) / Time.deltaTime;
				if (Time.deltaTime != 0f)
				{
					rb.linearVelocity = linearVelocity;
				}
				lastPosition = vector;
			}
			else
			{
				rb.linearVelocity = Vector2.zero;
			}
		}

		public override void RecoveryEnter()
		{
			base.RecoveryEnter();
			canDash = false;
		}

		private IEnumerator DelayedAttackCoroutine(float delay)
		{
			yield return new WaitForSeconds(delay);
			_attackPool = PoolManager.Instance.GetOrCreatePooler(_attackVFXPrefab);
			_attackVFX = _attackPool.GetOrCreate(null, activate: true);
			_attackVFX.transform.position = _attackBomb.transform.position;
			_attackVFX.SetStats(base.controller.stats);
			base.controller.stats.Speed = base.controller.stats.BaseSpeed;
			Action onEnd = null;
			_attackVFX.Trigger(onEnd);
			_attackBomb.Hide();
			_attackBomb.gameObject.SetActive(value: false);
			bombPooler.Return(_attackBomb);
			_attackBomb = null;
			yield return new WaitForSeconds(delay / 2f);
			_attackVFX.Stop();
			_attackPool.Return(_attackVFX);
		}

		protected void ReturnAttack(BulletProjectile bullet)
		{
			if (!(bullet == null))
			{
				bullet.OnReturn = null;
				bulletPooler.Return(bullet);
				if (_currentBullet != null && bullet.GetInstanceID() == _currentBullet.GetInstanceID())
				{
					_currentBullet = null;
				}
			}
		}

		public override void CancelAttack()
		{
			if (useBullet)
			{
				ReturnAttack(_currentBullet);
			}
			base.CancelAttack();
		}
	}
}
