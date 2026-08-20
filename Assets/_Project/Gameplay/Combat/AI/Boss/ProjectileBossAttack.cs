using System;
using System.Collections;
using Animancer;
using AstralShift.Helpers;
using AstralShift.QTI.Helpers.Attributes;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Boss
{
	public class ProjectileBossAttack : AnimatedBossAttack
	{
		[SerializeField]
		protected float speed;

		protected float _currentSpeed;

		[SerializeField]
		protected Vector2 direction;

		[SerializeField]
		protected float offscreenDespawnTimeout = 10f;

		protected Coroutine _movementCoroutine;

		protected Action despawnAction;

		[SerializeField]
		protected Transform markerRotationTransform;

		[SerializeField]
		protected bool constantSpeed = true;

		[SerializeField]
		[ConditionalHide("constantSpeed", false)]
		protected CustomAnimationCurve velocityCurve;

		[SerializeField]
		[ConditionalHide("constantSpeed", false)]
		protected float maxVelocityTimeout = 5f;

		protected AnimancerState _loopState;

		[SerializeField]
		[ConditionalHide("constantSpeed", false)]
		protected float animationSpeedAttenuation = 0.1f;

		[SerializeField]
		protected float despawnColliderDelay;

		[SerializeField]
		protected GameObject despawner;

		public virtual void Launch(float speed, Vector2 direction, bool rotateToDirection = true, float offScreenTimeout = 10f, float maxVelocityTimeout = 5f, Action despawnAction = null)
		{
			base.rotateToDirection = rotateToDirection;
			_currentSpeed = speed;
			_currentDirection = direction.normalized;
			offscreenDespawnTimeout = offScreenTimeout;
			this.despawnAction = despawnAction;
			this.maxVelocityTimeout = maxVelocityTimeout;
			StartCoroutine(Wait.SetTimeout(1f, delegate
			{
				despawner.gameObject.SetActive(value: true);
			}));
			StartMovement();
		}

		public virtual void Launch()
		{
			_currentSpeed = speed;
			_currentDirection = direction.normalized;
			StartMovement();
		}

		private void StartMovement()
		{
			if (_movementCoroutine != null)
			{
				StopCoroutine(_movementCoroutine);
			}
			_movementCoroutine = StartCoroutine(MovementCoroutine());
		}

		public void RotateToDirection(Vector2 direction)
		{
			if ((bool)rotationTransform)
			{
				float x = (isometricRotation ? isometricAngle : 0f);
				Vector3 eulerAngles = new Vector3(x, base.transform.eulerAngles.y, Vector2.SignedAngle(Vector2.right, direction));
				rotationTransform.eulerAngles = eulerAngles;
				if ((bool)markerRotationTransform)
				{
					markerRotationTransform.eulerAngles = eulerAngles;
				}
			}
			else
			{
				base.transform.eulerAngles = new Vector3(isometricAngle, base.transform.eulerAngles.y, Vector2.SignedAngle(Vector2.right, direction));
			}
		}

		private IEnumerator MovementCoroutine()
		{
			float offScreenElapsedTime = 0f;
			float elapsedVelocityTime = 0f;
			while (true)
			{
				float x = (isometricRotation ? isometricAngle : 0f);
				if (rotateToDirection)
				{
					RotateToDirection(_currentDirection);
				}
				else
				{
					base.transform.eulerAngles = new Vector3(x, base.transform.eulerAngles.y, base.transform.eulerAngles.z);
				}
				if (constantSpeed)
				{
					base.transform.position += rotationTransform.right * (_currentSpeed * Time.smoothDeltaTime);
				}
				else
				{
					float time = Mathf.Clamp01(elapsedVelocityTime / maxVelocityTimeout);
					float num = velocityCurve.animationCurve.Evaluate(time) * _currentSpeed;
					base.transform.position += rotationTransform.right * (num * Time.smoothDeltaTime);
					if (_loopState != null)
					{
						_loopState.Speed = num * animationSpeedAttenuation;
					}
					elapsedVelocityTime += Time.deltaTime;
				}
				if (ProCamera2DHelpers.IsWithinCameraBounds(base.transform.position))
				{
					offScreenElapsedTime = 0f;
					yield return null;
					continue;
				}
				offScreenElapsedTime += Time.deltaTime;
				if (offScreenElapsedTime > offscreenDespawnTimeout)
				{
					break;
				}
				yield return null;
			}
			_movementCoroutine = null;
			EndCallback();
		}

		public override void RunLoopAnimation()
		{
			if (loopAnimation.IsValid)
			{
				_loopState = animancer.Play(loopAnimation, loopAnimation.FadeDuration);
			}
		}

		protected virtual void EndCallback()
		{
			despawner.SetActive(value: false);
			despawnAction?.Invoke();
		}
	}
}
