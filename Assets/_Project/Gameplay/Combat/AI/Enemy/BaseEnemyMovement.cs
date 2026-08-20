using System;
using System.Collections;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public abstract class BaseEnemyMovement : MonoBehaviour
	{
		public BaseEnemyController enemyController;

		protected Transform _transform;

		protected Rigidbody2D _rigidbody;

		protected Vector3 _destination;

		protected Vector3 _direction;

		protected bool _canMove = true;

		private Coroutine _stopMovementCoroutine;

		private Coroutine _knockBackCoroutine;

		private Vector2 _startPoint;

		private Vector2 _endPoint;

		private Vector2 _lastPosition;

		private float _maxKnockbackTime = 1f;

		private float _knockBackTime;

		private float _elapsedTime;

		public Rigidbody2D Rigidbody => _rigidbody;

		public bool CanMove => _canMove;

		public virtual Vector3 Destination
		{
			get
			{
				return _destination;
			}
			set
			{
				_destination = value;
				if (_transform == null)
				{
					_transform = base.transform.parent;
				}
				_direction = _destination - (Vector3)enemyController.MovementCenterPosition;
				_direction.Normalize();
			}
		}

		public Vector3 Direction => _direction;

		public float Speed => enemyController.stats.Speed;

		public void Init(BaseEnemyController controller)
		{
			enemyController = controller;
		}

		public void SetTransform(Transform target)
		{
			_transform = target;
		}

		public void SetRigidBody(Rigidbody2D rigidbody)
		{
			_rigidbody = rigidbody;
		}

		public virtual void SetFacingDirection(Vector3 direction)
		{
			_direction = direction;
			_direction.Normalize();
		}

		public abstract void MovementUpdate();

		public void FreezeRigidbody(bool state)
		{
			if (state)
			{
				_rigidbody.constraints = RigidbodyConstraints2D.FreezeAll;
			}
			else
			{
				_rigidbody.constraints = RigidbodyConstraints2D.FreezeRotation;
			}
		}

		public void ResumeMovement()
		{
			_canMove = true;
		}

		public virtual void StopMovement(float timeout = 0f)
		{
			if (_stopMovementCoroutine != null)
			{
				StopCoroutine(_stopMovementCoroutine);
				_stopMovementCoroutine = null;
			}
			if (timeout == 0f)
			{
				_canMove = false;
				_rigidbody.linearVelocity = Vector2.zero;
			}
			else
			{
				_stopMovementCoroutine = StartCoroutine(StopMovementCoroutine(timeout));
			}
		}

		private IEnumerator StopMovementCoroutine(float timeout)
		{
			_canMove = false;
			_rigidbody.linearVelocity = Vector2.zero;
			yield return new WaitForSeconds(timeout);
			ResumeMovement();
			_stopMovementCoroutine = null;
		}

		public virtual void KnockBack(Vector2 direction, KnockbackSettings settings, Action onEnd, float knockbackMultiplier = 0f)
		{
			StopKnockBack();
			_startPoint = base.transform.position;
			Vector2 vector = (settings.fixedDirection ? settings.direction.normalized : direction);
			_endPoint = _startPoint + vector * (settings.distance * (1f + knockbackMultiplier) * enemyController.stats.KnockBackMultiplier);
			_lastPosition = _startPoint;
			_knockBackCoroutine = StartCoroutine(KnockBackCoroutine(settings, onEnd));
		}

		protected virtual IEnumerator KnockBackCoroutine(KnockbackSettings settings, Action onEnd)
		{
			WaitForFixedUpdate waitInstance = new WaitForFixedUpdate();
			_elapsedTime = 0f;
			_knockBackTime = _maxKnockbackTime / settings.speedMultiplier;
			while (enemyController.stats.KnockBackMultiplier != 0f)
			{
				_elapsedTime += Time.deltaTime;
				float time = _elapsedTime / _knockBackTime;
				if (_elapsedTime >= _knockBackTime)
				{
					break;
				}
				float t = settings.speedCurve.Evaluate(time);
				Vector2 vector = Vector2.Lerp(_startPoint, _endPoint, t);
				Vector2 linearVelocity = (vector - _lastPosition) / Time.fixedDeltaTime;
				_rigidbody.linearVelocity = linearVelocity;
				_lastPosition = vector;
				yield return waitInstance;
			}
			if (settings.Staggers)
			{
				yield return new WaitForSeconds(settings.staggerTime);
			}
			onEnd?.Invoke();
			_knockBackCoroutine = null;
		}

		protected virtual void StopKnockBack()
		{
			if (_knockBackCoroutine != null)
			{
				StopCoroutine(_knockBackCoroutine);
				_knockBackCoroutine = null;
			}
		}
	}
}
