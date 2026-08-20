using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Helpers;
using AstralShift.HellMaiden.Player.Attacks.ProjectileMovement;
using AstralShift.Pooling;
using AstralShift.QTI.Helpers.Attributes;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class ProjectileLauncherAttack : AnimatedAttack
	{
		public ProjectileAttack projectilePrefab;

		private GenericPooler<ProjectileAttack> _pooler;

		private List<ProjectileAttack> _attacks = new List<ProjectileAttack>();

		private PlayerMovement _player;

		private Vector3 positionOffset;

		private float LastAttackElapsedTime;

		[SerializeField]
		private Transform bulletPivot;

		[SerializeField]
		private float projectileSpeed = 27f;

		[SerializeField]
		private int hitMaxCount = 1;

		[SerializeField]
		private float multipleProjectilesFireRate = 0.1f;

		[SerializeField]
		private bool homingProjectile;

		[Header("Cone Detection Settings")]
		[SerializeField]
		[ConditionalHide("homingProjectile", true)]
		private float minDetectionRadius;

		[SerializeField]
		[ConditionalHide("homingProjectile", true)]
		private float maxDetectionRadius = 80f;

		[SerializeField]
		[ConditionalHide("homingProjectile", true)]
		private float coneAngle = 90f;

		private float _currentAngle;

		[SerializeField]
		private float lerpSpeed = 6f;

		private bool _drawGizmos;

		private void OnEnable()
		{
			_player = GameDirector.Instance.Player;
		}

		private void Update()
		{
			float b = Mathf.Atan2(_player.attackDirection.y, _player.attackDirection.x) * 57.29578f;
			_currentAngle = Mathf.LerpAngle(_currentAngle, b, Time.deltaTime * lerpSpeed);
			Vector3 vector = new Vector3(Mathf.Cos(_currentAngle * (MathF.PI / 180f)), Mathf.Sin(_currentAngle * (MathF.PI / 180f)), 0f);
			UpdateRotation(vector);
		}

		public void ShootProjectile()
		{
			StartCoroutine(ShootingRoutine());
		}

		private IEnumerator ShootingRoutine()
		{
			int projectileCount = _behaviour.ProjectileCountValue;
			List<BaseEnemyController> enemyTargets = new List<BaseEnemyController>();
			List<BaseEnemyController> availableTargets = new List<BaseEnemyController>();
			if (homingProjectile)
			{
				enemyTargets = AIHelpers.FindEnemiesInConeRangeOrderedByDistance(bulletPivot.position, _player.attackDirection.normalized, minDetectionRadius, maxDetectionRadius, coneAngle);
				availableTargets = enemyTargets.ToArray().ToList();
			}
			for (int i = 0; i < projectileCount; i++)
			{
				ProjectileAttack projectile = GetOrCreateAttack();
				projectile.gameObject.SetActive(value: true);
				projectile.transform.position = bulletPivot.transform.position + (Vector3)_player.attackDirection.normalized;
				projectile.transform.rotation = base.transform.rotation;
				if (homingProjectile && projectile.TryGetComponent<PM_Homing>(out var component))
				{
					projectile.OnBeforeEnd = delegate
					{
						projectile.hitbox.Toggle(state: false);
					};
					if (availableTargets.Count > 0)
					{
						int index = ((i != 0) ? Mathf.Clamp(availableTargets.Count / (i + 1), 0, availableTargets.Count - 1) : 0);
						BaseEnemyController baseEnemyController = availableTargets[index];
						availableTargets.Remove(baseEnemyController);
						component.InitHoming(baseEnemyController);
					}
					else
					{
						component.InitHoming(enemyTargets.FirstOrDefault());
					}
				}
				projectile.Attack(_player.attackDirection.normalized, projectileSpeed, hitMaxCount, rotateToDirection: true);
				yield return new WaitForSeconds(multipleProjectilesFireRate);
			}
		}

		protected ProjectileAttack GetOrCreateAttack()
		{
			if (_pooler == null)
			{
				_pooler = PoolManager.Instance.GetOrCreatePooler(projectilePrefab);
			}
			ProjectileAttack attack = _pooler.GetOrCreate(null);
			if (!_attacks.Contains(attack))
			{
				_attacks.Add(attack);
			}
			Action onEnd = delegate
			{
				_attacks.Remove(attack);
				_pooler.Return(attack);
			};
			attack.Init(_behaviour, delegate
			{
				attack.hitbox.Toggle(state: true);
			}, onEnd);
			return attack;
		}

		protected override void EndCallback()
		{
			_onEnd?.Invoke();
		}

		private void OnDrawGizmosSelected()
		{
			if (_drawGizmos && homingProjectile && base.gameObject.activeInHierarchy)
			{
				DrawConeGizmo(bulletPivot.position, _player.attackDirection.normalized, maxDetectionRadius, coneAngle);
			}
		}

		public static void DrawConeGizmo(Vector2 origin, Vector2 direction, float range, float angle)
		{
			Gizmos.color = Color.yellow;
			float num = angle * 0.5f;
			Vector2 vector = Quaternion.Euler(0f, 0f, num) * direction.normalized;
			Vector2 vector2 = Quaternion.Euler(0f, 0f, 0f - num) * direction.normalized;
			Vector2 vector3 = origin + vector * range;
			Vector2 vector4 = origin + vector2 * range;
			Gizmos.DrawLine(origin, vector3);
			Gizmos.DrawLine(origin, vector4);
			int num2 = 20;
			Vector2 vector5 = vector3;
			for (int i = 1; i <= num2; i++)
			{
				float num3 = (float)i / (float)num2;
				float z = num - angle * num3;
				Vector2 vector6 = Quaternion.Euler(0f, 0f, z) * direction.normalized;
				Vector2 vector7 = origin + vector6 * range;
				Gizmos.DrawLine(vector5, vector7);
				vector5 = vector7;
			}
		}
	}
}
