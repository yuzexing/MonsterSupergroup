using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Interactions.Demos.FPPuzzleDemo
{
	public class TranslateInteraction : Interaction, IGravityField
	{
		public Transform startTransform;

		public Transform endTransform;

		public float speed = 2f;

		public int moveCount = -1;

		public bool canReverse;

		public bool invertOnInteract;

		public bool useFixedUpdate;

		public bool onEndWaitToFinish;

		private int _currentMoveCount;

		private Vector3 _lastPosition;

		private Vector3 _currentPosition;

		private float _movementDelta;

		private float _easedLerpFactor;

		private float _velocityDelta;

		private float _sign = 1f;

		private bool _enabled;

		public bool IsEnabled => _enabled;

		public override bool CanInteract()
		{
			return true;
		}

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			if (invertOnInteract)
			{
				if (_enabled)
				{
					_sign = 0f - _sign;
				}
				_enabled = true;
				_currentMoveCount = 0;
			}
			else
			{
				_enabled = !_enabled;
				_currentMoveCount = 0;
			}
			if (!onEndWaitToFinish)
			{
				OnEnd();
			}
		}

		public Vector3 GetMovementDelta()
		{
			if (_enabled)
			{
				Vector3 vector = Vector3.Lerp(startTransform.position, endTransform.position, _easedLerpFactor);
				Debug.Log(vector - _lastPosition);
				return vector - _lastPosition;
			}
			return Vector3.zero;
		}

		public void FixedUpdate()
		{
			if (!useFixedUpdate || !_enabled)
			{
				return;
			}
			if (moveCount != -1 && _currentMoveCount >= moveCount)
			{
				_enabled = false;
				_currentMoveCount = 0;
				if (onEndWaitToFinish)
				{
					OnEnd();
				}
				return;
			}
			float num = Vector3.Distance(startTransform.position, endTransform.position);
			_movementDelta += _sign * speed * Time.fixedDeltaTime;
			float num2 = _movementDelta / num;
			_easedLerpFactor = Mathf.SmoothStep(0f, 1f, num2);
			_lastPosition = base.transform.position;
			_currentPosition = Vector3.Lerp(startTransform.position, endTransform.position, _easedLerpFactor);
			base.transform.position = _currentPosition;
			if (num2 > 1f)
			{
				num2 = 1f;
				if (canReverse)
				{
					_sign = 0f - _sign;
				}
				if (moveCount != -1)
				{
					_currentMoveCount++;
				}
			}
			if (num2 < 0f)
			{
				num2 = 0f;
				if (canReverse)
				{
					_sign = 0f - _sign;
				}
				if (moveCount != -1)
				{
					_currentMoveCount++;
				}
			}
		}

		public void Update()
		{
			if (useFixedUpdate || !_enabled)
			{
				return;
			}
			if (moveCount != -1 && _currentMoveCount >= moveCount)
			{
				_enabled = false;
				_currentMoveCount = 0;
				if (onEndWaitToFinish)
				{
					OnEnd();
				}
				return;
			}
			float num = Vector3.Distance(startTransform.position, endTransform.position);
			_movementDelta += _sign * speed * Time.smoothDeltaTime;
			float num2 = _movementDelta / num;
			_easedLerpFactor = Mathf.SmoothStep(0f, 1f, num2);
			_lastPosition = base.transform.position;
			_currentPosition = Vector3.Lerp(startTransform.position, endTransform.position, _easedLerpFactor);
			base.transform.position = _currentPosition;
			if (num2 > 1f)
			{
				num2 = 1f;
				if (canReverse)
				{
					_sign = 0f - _sign;
				}
				if (moveCount != -1)
				{
					_currentMoveCount++;
				}
			}
			if (num2 < 0f)
			{
				num2 = 0f;
				if (canReverse)
				{
					_sign = 0f - _sign;
				}
				if (moveCount != -1)
				{
					_currentMoveCount++;
				}
			}
		}
	}
}
