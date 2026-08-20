using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class EnemyWarningStep : MonoBehaviour
	{
		[SerializeField]
		private EnemyAttackMelee _attackMelee;

		[SerializeField]
		private Rigidbody2D _rigidbody;

		public float stepDistance = 0.5f;

		private EnemyController _controller;

		private void Start()
		{
			_attackMelee.OnWarningTick = AttackWarningTick;
			_attackMelee.onAttackWarningExit = AttackWarningEnd;
			_controller = _attackMelee.controller;
		}

		private void AttackWarningTick(float warningTime)
		{
			_controller.Movement.FreezeRigidbody(state: false);
			Vector2 position = _rigidbody.position;
			Vector2 b = position + _controller.FacingDirection * stepDistance;
			_rigidbody.MovePosition(Vector2.Lerp(position, b, warningTime));
		}

		private void AttackWarningEnd()
		{
			Vector2 position = _rigidbody.position + _controller.FacingDirection * stepDistance;
			_rigidbody.MovePosition(position);
			_controller.Movement.FreezeRigidbody(state: true);
		}
	}
}
