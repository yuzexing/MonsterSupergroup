using Animancer;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class OvidSummonMover : MonoBehaviour
	{
		[SerializeField]
		private Rigidbody2D rb;

		[SerializeField]
		private Transform isoPivot;

		[SerializeField]
		private Transform rotationPivot;

		[SerializeField]
		private float cacoonSpeedMultiplier = 2f;

		[SerializeField]
		private float maxMoveSpeed = 12f;

		[SerializeField]
		private float minMoveSpeed = 0.5f;

		[SerializeField]
		private float maxSpeedDistance = 20f;

		[SerializeField]
		private CustomAnimationCurve accelerationCurve;

		[SerializeField]
		private float decelerationMultiplier = 2f;

		[SerializeField]
		private float angleOffset = 180f;

		[SerializeField]
		private float rotationSmoothing = 20f;

		[SerializeField]
		private float tiltAngle = -45f;

		[SerializeField]
		private float hoverFrequency = 2f;

		[SerializeField]
		private float hoverAmplitude = 0.5f;

		[SerializeField]
		private float hoverYAxisFactor = 0.5f;

		[SerializeField]
		private ClipTransition moveAnimation;

		[SerializeField]
		private float minAnimSpeed = 1f;

		[SerializeField]
		private float maxAnimSpeed = 2f;

		private AnimancerState _moveState;

		private float _hoverTimer;

		private const float DirectionMagnitudeThreshold = 0.01f;

		protected SummonAIBehaviour _behaviour;

		public Transform GetRotationPivot()
		{
			return rotationPivot;
		}

		public virtual void Init(SummonAIBehaviour behaviour)
		{
			_behaviour = behaviour;
		}

		public void Move(Vector2 direction, float distance, float stopDistance)
		{
			float t = Mathf.Clamp01((distance - stopDistance) / (maxSpeedDistance - stopDistance));
			float num = accelerationCurve.EasePercentage(t);
			float num2 = maxMoveSpeed * num;
			if (distance > stopDistance)
			{
				num2 = Mathf.Max(num2, minMoveSpeed);
			}
			rb.linearVelocity = direction * num2;
			Rotate(direction);
			UpdateTilt();
			UpdateHover();
		}

		public void Stop(bool immediately = false)
		{
			if (immediately)
			{
				rb.linearVelocity = Vector2.zero;
			}
			else
			{
				rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, Vector2.zero, Time.smoothDeltaTime * maxMoveSpeed * decelerationMultiplier);
			}
			UpdateTilt();
			UpdateHover();
		}

		public void MoveCacoon(Vector2 direction, float distance, float stopDistance)
		{
			float t = Mathf.Clamp01((distance - stopDistance) / (maxSpeedDistance - stopDistance));
			float num = accelerationCurve.EasePercentage(t);
			float num2 = maxMoveSpeed * num * cacoonSpeedMultiplier;
			if (distance > stopDistance)
			{
				num2 = Mathf.Max(num2, minMoveSpeed);
			}
			rb.linearVelocity = direction * num2;
		}

		public void StopCacoon()
		{
			rb.linearVelocity = Vector2.zero;
		}

		private void Rotate(Vector2 direction)
		{
			if (!(direction.sqrMagnitude <= 0.01f))
			{
				float b = Mathf.Atan2(direction.x, direction.y) * 57.29578f + angleOffset;
				float y = Mathf.LerpAngle(rotationPivot.localEulerAngles.y, b, Time.smoothDeltaTime * rotationSmoothing);
				rotationPivot.localRotation = Quaternion.Euler(rotationPivot.localEulerAngles.x, y, 0f);
			}
		}

		private void UpdateTilt()
		{
			float t = Mathf.Clamp01(rb.linearVelocity.magnitude / maxMoveSpeed);
			float x = Mathf.LerpAngle(0f, tiltAngle, t);
			rotationPivot.localRotation = Quaternion.Euler(x, rotationPivot.localEulerAngles.y, 0f);
		}

		private void UpdateHover()
		{
			_hoverTimer += Time.smoothDeltaTime * hoverFrequency;
			float x = Mathf.Cos(_hoverTimer) * hoverAmplitude;
			float y = Mathf.Sin(_hoverTimer) * hoverAmplitude * hoverYAxisFactor;
			isoPivot.localPosition = new Vector3(x, y, 0f);
		}

		public void UpdateAnimation()
		{
			_moveState = _behaviour.Animancer.Play(moveAnimation);
			float t = Mathf.Clamp01(rb.linearVelocity.magnitude / maxMoveSpeed);
			_moveState.Speed = Mathf.Lerp(minAnimSpeed, maxAnimSpeed, t);
		}
	}
}
