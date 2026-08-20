using AstralShift.HellMaiden.Combat;
using AstralShift.Pooling;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class EnemyProjectileAttack : EnemyAttack
	{
		public Transform bulletPosition;

		public EnemyController enemyController;

		protected BulletProjectile _currentBullet;

		public BulletProjectile bulletPrefab;

		protected GenericPooler<BulletProjectile> bulletPooler;

		protected Vector2 direction;

		[Tooltip("Rotate attack to face target")]
		[SerializeField]
		protected bool rotateAttack;

		public override void AttackWarningEnter()
		{
			base.AttackWarningEnter();
			float num = Mathf.Abs(bulletPosition.transform.localPosition.x);
			bulletPosition.transform.localPosition = new Vector3((enemyController.FacingDirection.x < 0f) ? (0f - num) : num, bulletPosition.transform.localPosition.y, bulletPosition.transform.localPosition.z);
			bulletPooler = PoolManager.Instance.GetOrCreatePooler(bulletPrefab);
			BulletProjectile bulletProjectile = bulletPooler.GetOrCreate(bulletPosition);
			direction = base.Target.position - bulletPosition.position;
			if (rotateAttack)
			{
				float angle = Mathf.Atan2(direction.y, direction.x) * 57.29578f;
				bulletProjectile.transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
			}
			bulletProjectile.OnReturn = delegate
			{
				ReturnAttack(bulletProjectile);
			};
			bulletProjectile.ShooterController = base.controller;
			bulletProjectile.transform.localPosition = Vector3.zero;
			bulletProjectile.fired = false;
			bulletProjectile.SetStats(base.controller.stats);
			bulletProjectile.gameObject.SetActive(value: true);
			_currentBullet = bulletProjectile;
		}

		public override void AttackEnter()
		{
			base.AttackEnter();
			if (!(_currentBullet == null))
			{
				_currentBullet.transform.parent = null;
				_currentBullet.Fire(direction);
			}
		}

		protected virtual void ReturnAttack(BulletProjectile bullet)
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
			if (!(_currentBullet == null))
			{
				base.controller.lastAttackTime = Time.time;
				ReturnAttack(_currentBullet);
				_currentBullet = null;
			}
		}
	}
}
