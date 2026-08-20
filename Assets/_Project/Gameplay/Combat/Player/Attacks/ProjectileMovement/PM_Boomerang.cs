using System;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.Helpers.Attributes;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks.ProjectileMovement
{
	public class PM_Boomerang : PM_Base
	{
		private Transform rotationTransform;

		[SerializeField]
		private bool rotate;

		public Animator animator;

		private Vector2 _direction = Vector2.right;

		public float distance = 10f;

		public float arcOffset = 3f;

		[ReadOnly]
		public float travelTime = 10f;

		protected Transform backTransform;

		private Vector2 origin;

		private Vector2 controlPoint;

		private Vector2 farthestPoint;

		private float elapsedTime;

		private float outboundFraction = 0.5f;

		private float returnFraction = 0.3f;

		private float returnTime;

		private float returnElapsed;

		private Vector2 returnStart;

		private bool returning;

		private float currentReturnSpeed;

		public float playerCloseness = 1.2f;

		public float acceleration = 4f;

		private bool _startedExpiring;

		[SerializeField]
		private Vector3 positionOffset;

		public float playerMaxCloseness = 1.2f;

		public override void Init(Vector2 direction, Transform rotationTransform, float speed, float despawnTimeout, Transform originTransform = null)
		{
			origin = base.transform.position;
			returning = false;
			_startedExpiring = false;
			animator.speed = 1f;
			backTransform = originTransform;
			_direction = direction.normalized;
			Vector2 vector = new Vector2(0f - _direction.y, _direction.x);
			controlPoint = origin + _direction * distance + vector * arcOffset;
			travelTime = despawnTimeout;
			MonoBehaviour.print("travelTime = " + travelTime);
			elapsedTime = 0f;
		}

		public override void MovementUpdate(Vector2 direction, Transform rotationTransform, float speed)
		{
			elapsedTime += Time.deltaTime;
			if (!returning)
			{
				float num = elapsedTime / travelTime;
				if (num <= 0.5f)
				{
					float num2 = num / 0.5f;
					Vector2 vector = origin;
					Vector2 vector2 = controlPoint;
					Vector2 vector3 = (farthestPoint = origin + _direction.normalized * distance);
					Vector2 vector4 = Mathf.Pow(1f - num2, 2f) * vector + 2f * (1f - num2) * num2 * vector2 + Mathf.Pow(num2, 2f) * vector3;
					base.transform.position = vector4;
					Vector2 velocity = 2f * (1f - num2) * (vector2 - vector) + 2f * num2 * (vector3 - vector2);
					RotateToVelocity(velocity);
				}
				else
				{
					returning = true;
					returnStart = base.transform.position;
					returnElapsed = 0f;
					returnTime = travelTime * returnFraction;
					currentReturnSpeed = 0f;
				}
				return;
			}
			returnElapsed += Time.deltaTime;
			Vector2 vector5 = (Vector2)backTransform.position + (Vector2)positionOffset;
			Vector2 vector6 = vector5 - (Vector2)base.transform.position;
			float num3 = Mathf.Max(0.001f, returnTime - returnElapsed);
			float b = vector6.magnitude / num3;
			currentReturnSpeed += acceleration * Time.deltaTime;
			currentReturnSpeed = Mathf.Max(currentReturnSpeed, b);
			float num4 = Mathf.Clamp01(returnElapsed / returnTime);
			Vector2 vector7 = Vector2.Lerp(returnStart, vector5, num4);
			Vector2 normalized = (vector5 - returnStart).normalized;
			Vector2 vector8 = new Vector2(0f - normalized.y, normalized.x);
			float num5 = Mathf.Sin(num4 * -MathF.PI) * (arcOffset / 2f);
			num5 *= -1f;
			Vector2 vector9 = vector7 + vector8 * num5;
			Vector2 velocity2 = vector9 - (Vector2)base.transform.position;
			animator.speed = Mathf.Clamp(currentReturnSpeed / 21f, 1f, 20f);
			if (!_startedExpiring && (vector6.magnitude <= playerCloseness || velocity2.sqrMagnitude >= vector6.sqrMagnitude))
			{
				GetComponent<ProjectileAttack>()?.OnExpireHitEffect();
				GetComponent<BulletProjectile>()?.StopBulletMovement();
				_startedExpiring = true;
			}
			if (vector6.magnitude <= playerMaxCloseness)
			{
				base.transform.position = vector5;
				return;
			}
			base.transform.position = vector9;
			RotateToVelocity(velocity2);
		}

		private void RotateToVelocity(Vector2 velocity)
		{
			if (rotate && velocity.sqrMagnitude > 0.0001f)
			{
				float z = Mathf.Atan2(velocity.y, velocity.x) * 57.29578f;
				rotationTransform.rotation = Quaternion.Euler(0f, 0f, z);
			}
		}
	}
}
