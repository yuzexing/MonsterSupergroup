using System;
using System.Collections;
using Animancer;
using AstralShift.HellMaiden.AI.Boss;
using AstralShift.HellMaiden.Combat;
using AstralShift.Pooling;
using UnityEngine;
using UnityEngine.Serialization;

namespace AstralShift.HellMaiden.AI.Enemy.Boss
{
	public class Shooter : MonoBehaviour
	{
		public Transform bulletParent;

		private BulletProjectile _currentBullet;

		public BulletProjectile bulletPrefab;

		protected GenericPooler<BulletProjectile> bulletPooler;

		public int numberOfBarrages;

		public float bulletInterval;

		[FormerlySerializedAs("bulletsPerArc")]
		public int bulletsPerBarrage = 4;

		public float spawnRadius = 2f;

		public float rotationSpeed = 90f;

		public float attackAngle = 180f;

		private float _currentAngleOffset;

		[Header("Character Animations")]
		[SerializeField]
		private BossAnimator _animator;

		public ClipTransition shootAnimation;

		private bool _stopped;

		public BaseEnemyController enemyController;

		public virtual void ShootBullets()
		{
			if (_stopped)
			{
				return;
			}
			bulletPooler = PoolManager.Instance.GetOrCreatePooler(bulletPrefab);
			if (_animator != null)
			{
				shootAnimation.Events.OnEnd = delegate
				{
					StartCoroutine(ShootingRoutine());
					_animator.Animancer.Layers[1].Stop();
				};
				_animator.Animancer.Layers[1].Play(shootAnimation);
			}
			else
			{
				StartCoroutine(ShootingRoutine());
			}
		}

		public void StopShooting()
		{
			StopAllCoroutines();
			_stopped = true;
		}

		private IEnumerator ShootingRoutine()
		{
			Vector2 direction = GameDirector.Instance.Player.EnemyAttackTarget.position - bulletParent.transform.position;
			WaitForSeconds waitForSeconds = new WaitForSeconds(bulletInterval);
			_currentAngleOffset = (0f - attackAngle) / 2f;
			for (int i = 0; i < numberOfBarrages; i++)
			{
				for (int j = 0; j < bulletsPerBarrage; j++)
				{
					float num = Vector2.SignedAngle(base.transform.right, direction);
					float f = (attackAngle / (float)bulletsPerBarrage * (float)j + _currentAngleOffset + num) * (MathF.PI / 180f);
					Vector3 vector = base.transform.position + new Vector3(Mathf.Cos(f), Mathf.Sin(f), 0f) * spawnRadius;
					BulletProjectile bulletProjectile = bulletPooler.GetOrCreate(bulletParent);
					bulletProjectile.OnReturn = delegate
					{
						bulletPooler.Return(bulletProjectile);
					};
					bulletProjectile.transform.position = vector;
					bulletProjectile.ShooterController = enemyController;
					bulletProjectile.fired = false;
					if (enemyController != null)
					{
						bulletProjectile.SetStats(enemyController.stats);
					}
					bulletProjectile.gameObject.SetActive(value: true);
					bulletProjectile.transform.parent = null;
					Vector3 normalized = (vector - base.transform.position).normalized;
					bulletProjectile.Fire(normalized);
				}
				if (attackAngle > 0f)
				{
					_currentAngleOffset += rotationSpeed * Time.deltaTime;
					_currentAngleOffset %= attackAngle;
				}
				yield return waitForSeconds;
			}
		}
	}
}
