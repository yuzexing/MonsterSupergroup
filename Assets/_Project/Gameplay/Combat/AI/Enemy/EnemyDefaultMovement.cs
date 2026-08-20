using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class EnemyDefaultMovement : BaseEnemyMovement
	{
		private bool _ignorePhysics;

		private bool _isInOptimizedState;

		public override void MovementUpdate()
		{
			if (!_transform || !_canMove)
			{
				return;
			}
			if (enemyController.IsInStoppingRange)
			{
				_rigidbody.linearVelocity = Vector2.zero;
				return;
			}
			if (_ignorePhysics)
			{
				if ((_rigidbody.constraints & RigidbodyConstraints2D.FreezeAll) != RigidbodyConstraints2D.FreezeAll)
				{
					_rigidbody.simulated = false;
					_transform.position += (_direction * base.Speed + (Vector3)enemyController.windDirection + (Vector3)enemyController.ultimateWindInteraction) * Time.fixedDeltaTime;
					_isInOptimizedState = true;
				}
				else
				{
					_isInOptimizedState = false;
				}
				return;
			}
			_rigidbody.simulated = true;
			_rigidbody.linearVelocity = _direction * base.Speed;
			_isInOptimizedState = false;
			if (enemyController.enemyFlyingType || enemyController.forceWindInteraction)
			{
				if (enemyController.forceWindInteraction)
				{
					_rigidbody.linearVelocity = Vector2.zero;
				}
				_rigidbody.linearVelocity += (enemyController.windDirection + enemyController.ultimateWindInteraction) * enemyController.stats.WindMultiplier;
			}
		}

		public bool IsInOptimizedState()
		{
			return _isInOptimizedState;
		}

		public void SetOptimizations(bool state)
		{
			_ignorePhysics = state;
		}
	}
}
