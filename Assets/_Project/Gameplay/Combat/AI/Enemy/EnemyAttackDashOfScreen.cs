using AstralShift.Helpers;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class EnemyAttackDashOfScreen : EnemyAttackDash
	{
		private SpriteRenderer sr;

		[SerializeField]
		private SpriteRenderer shadow;

		private Quaternion originalRotation;

		public bool horizontalDash;

		public bool rotateToDirection;

		public float extraDistanceFromCamera = 25f;

		[SerializeField]
		private float maxDashSpeed = 60f;

		[SerializeField]
		private float accelerationTime = 20f;

		[SerializeField]
		private float offsetToTarget = 5f;

		[SerializeField]
		private float targetVelocityOffsetMultiplier = 1.5f;

		[SerializeField]
		private float jitter = 0.2f;

		public override bool OverrideKnockback => true;

		private void Start()
		{
			sr = sprite.GetComponent<SpriteRenderer>();
			damageInteraction.enemyStats = base.controller.stats;
			collider = base.controller.collider;
			defaultLayerMask = collider.excludeLayers;
		}

		public override void AttackWarningEnter()
		{
			base.AttackWarningEnter();
			if (horizontalDash)
			{
				float num;
				if (base.transform.position.x > base.Target.position.x)
				{
					_direction = Vector2.left;
					offsetToTarget = ((offsetToTarget < 0f) ? (0f - offsetToTarget) : offsetToTarget);
					num = Random.Range(0f, jitter);
				}
				else
				{
					_direction = Vector2.right;
					offsetToTarget = ((offsetToTarget > 0f) ? (0f - offsetToTarget) : offsetToTarget);
					num = Random.Range(0f - jitter, 0f);
				}
				float num2 = GameDirector.Instance.Player.GetLinearVelocity().y * targetVelocityOffsetMultiplier;
				if (num2 != 0f)
				{
					num2 += offsetToTarget;
				}
				num2 += num;
				Vector2 vector = new Vector2(base.transform.position.x, base.Target.position.y + num2);
				if (!ProCamera2DHelpers.IsWithinCameraBounds(vector))
				{
					base.transform.position = vector;
				}
				endPoint = new Vector2((ProCamera2DHelpers.GetCameraExtents().x + extraDistanceFromCamera) * _direction.x + base.transform.position.x, base.transform.position.y);
			}
			else
			{
				_direction = base.Target.transform.position - _attack.transform.position;
				endPoint = ProCamera2DHelpers.GetPointOutsideCameraByPlayer(_direction, extraDistanceFromCamera, base.controller.LocalBounds, base.Target.transform.position);
			}
			startPoint = base.transform.position;
			float z = Mathf.Atan2(_direction.y, _direction.x) * 57.29578f;
			Quaternion to = Quaternion.Euler(0f, 0f, z);
			_warning.transform.rotation = Quaternion.RotateTowards(_warning.transform.rotation, to, 360f);
		}

		public override void AttackEnter()
		{
			lastPosition = rb.position;
			if (rotateToDirection)
			{
				float num = Mathf.Atan2(_direction.y, _direction.x) * 57.29578f;
				if (sr.flipX)
				{
					sprite.rotation = Quaternion.Euler(0f, 0f, num);
					shadow.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
				}
				else
				{
					sprite.rotation = Quaternion.Euler(0f, 0f, num + 180f);
					shadow.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
				}
			}
			rb.simulated = true;
			base.AttackEnter();
		}

		public override void AttackTick()
		{
			float num = Time.time - _attackStartTime;
			float time = Mathf.Clamp01(num / accelerationTime);
			float num2 = movementCurve.Evaluate(time) * maxDashSpeed;
			rb.linearVelocity = _direction.normalized * num2;
			if (num >= base.AttackTime && !ProCamera2DHelpers.IsWithinCameraBounds(base.transform.position, 1.3f))
			{
				rb.linearVelocity = Vector2.zero;
				onAttackEnd?.Invoke();
			}
		}

		public override void AttackExit()
		{
			base.AttackExit();
			sprite.rotation = Quaternion.Euler(0f, 0f, 0f);
			shadow.transform.rotation = originalRotation;
		}

		private void OnDrawGizmos()
		{
			Gizmos.DrawLine(base.transform.position, endPoint);
		}
	}
}
