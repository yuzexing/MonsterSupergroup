using System;
using AstralShift.HellMaiden.AI.Enemy;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks.ProjectileMovement
{
	public class PM_Homing : PM_Base
	{
		private PlayerMovement playerTarget;

		[SerializeField]
		private bool followPlayer;

		[SerializeField]
		private float turnSpeed = 15f;

		[SerializeField]
		private float elapsedTime;

		private ProjectileAttack _projectileAttack;

		private BulletProjectile _bulletAttack;

		private Vector2 _targetDirection;

		private Vector2 _startingDirection;

		private Vector2 _currentDirection;

		[SerializeField]
		private float updateTime = 0.2f;

		private bool hasTarget;

		public float maxTurnSpeed = 90f;

		[SerializeField]
		private float minDistanceToTarget = 0.1f;

		public BaseEnemyController Target { get; set; }

		public override void Init(Vector2 direction, Transform rotationTransform, float speed, float despawnTimeout, Transform originTransform = null)
		{
			elapsedTime = 0f;
			_startingDirection = direction;
			_currentDirection = direction;
			if (followPlayer)
			{
				_bulletAttack = GetComponent<BulletProjectile>();
				InitHoming(null);
			}
			else
			{
				_projectileAttack = GetComponent<ProjectileAttack>();
			}
		}

		public void InitHoming(BaseEnemyController enemyTarget)
		{
			Target = enemyTarget;
			hasTarget = IsTargetAlive();
			if (hasTarget)
			{
				_targetDirection = (GetTargetPosition() - (Vector2)base.transform.position).normalized;
				InvokeRepeating("UpdateDirection", updateTime, updateTime);
			}
		}

		public override void MovementUpdate(Vector2 direction, Transform rotationTransform, float speed)
		{
			hasTarget = hasTarget && IsTargetAlive();
			if (!hasTarget)
			{
				base.transform.position += (Vector3)(_currentDirection * (speed * Time.smoothDeltaTime));
				_projectileAttack.UpdateRotation(_currentDirection);
				return;
			}
			elapsedTime += Time.deltaTime;
			_currentDirection = Vector2.Lerp(_startingDirection, _targetDirection, elapsedTime / updateTime).normalized;
			Vector2 vector = _currentDirection * (speed * Time.smoothDeltaTime);
			base.transform.position += (Vector3)vector;
			if (!followPlayer)
			{
				_projectileAttack.UpdateRotation(_currentDirection);
			}
			if (Vector3.Distance(base.transform.position, GetTargetPosition()) < minDistanceToTarget)
			{
				Target = null;
			}
		}

		private void UpdateDirection()
		{
			hasTarget = hasTarget && IsTargetAlive();
			if (hasTarget)
			{
				float maxRadiansDelta = maxTurnSpeed * (MathF.PI / 180f);
				_targetDirection = Vector3.RotateTowards(_currentDirection, (GetTargetPosition() - (Vector2)base.transform.position).normalized, maxRadiansDelta, 0f);
				_startingDirection = _currentDirection;
			}
		}

		private bool IsTargetAlive()
		{
			if (!followPlayer)
			{
				if (Target != null)
				{
					return !Target.IsDead;
				}
				return false;
			}
			return true;
		}

		private Vector2 GetTargetPosition()
		{
			if (followPlayer)
			{
				return GameDirector.Instance.Player.EnemyAttackTarget.position;
			}
			return Target.GetHurtBoxPosition();
		}
	}
}
