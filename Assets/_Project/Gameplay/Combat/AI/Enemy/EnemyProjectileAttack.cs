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

        public IEnemyProjectileExecution NetworkExecution { get; set; }
        public Vector2 LockedDirection => direction;
        public bool ProjectileEmitted { get; private set; }
        public bool RotateAttack => rotateAttack;

		[Tooltip("Rotate attack to face target")]
		[SerializeField]
		protected bool rotateAttack;

		public override void AttackWarningEnter()
		{
			base.AttackWarningEnter();
			ProjectileEmitted = false;
			if (NetworkExecution != null)
			{
                Vector2 facing = (Vector2)Target.position - (Vector2)controller.transform.position;
                AlignProjectileOrigin(facing);
                direction = ((Vector2)Target.position - (Vector2)bulletPosition.position).normalized;
                if (direction.sqrMagnitude < .0001f) direction = Vector2.right;
                NetworkExecution.ShowCharge();
                return;
			}
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
            if (NetworkExecution != null)
            {
                if (!ProjectileEmitted) { ProjectileEmitted = true; NetworkExecution.Launch(direction); }
                return;
            }
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
            if (NetworkExecution != null)
            {
                if (controller != null && (controller.CurrentAttackPresentationPhase == EnemyAttackPresentationPhase.Warning ||
                    controller.CurrentAttackPresentationPhase == EnemyAttackPresentationPhase.Active)) controller.lastAttackTime = Time.time;
                NetworkExecution.CancelCharge(); return;
            }
			if (!(_currentBullet == null))
			{
				base.controller.lastAttackTime = Time.time;
				ReturnAttack(_currentBullet);
				_currentBullet = null;
			}
		}

        public void RestoreProjectileSimulation(EnemyActionState state, EnemyAttackPresentationPhase phase)
        {
            if (NetworkExecution == null) return;
            direction = state.ProjectileDirection;
            AlignProjectileOrigin(direction);
            ProjectileEmitted = state.ProjectileEmitted;
            NetworkExecution.CancelCharge();
            if (phase == EnemyAttackPresentationPhase.Warning) NetworkExecution.ShowCharge();
            else if (phase == EnemyAttackPresentationPhase.Active && !ProjectileEmitted) AttackEnter();
        }

        public void AlignProjectileOrigin(Vector2 facing)
        {
            float x = Mathf.Abs(bulletPosition.localPosition.x);
            bulletPosition.localPosition = new Vector3(facing.x < 0 ? -x : x, bulletPosition.localPosition.y, bulletPosition.localPosition.z);
        }

        private void OnDisable() { NetworkExecution?.CancelCharge(); }
	}
}
