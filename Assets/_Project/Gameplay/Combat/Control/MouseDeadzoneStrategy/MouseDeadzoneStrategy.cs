using System;
using Rewired;
using UnityEngine;

namespace AstralShift.Control.MouseDeadzoneStrategy
{
	public class MouseDeadzoneStrategy
	{
		private float _movementTimer;

		private float _idleTimer;

		private Vector2 _lastMousePosition;

		private bool _isMovementDetected;

		private bool _isSwitchingToMouse;

		private bool _isClickPending;

		public float MovementDeadzoneTime { get; set; } = 0.33f;

		public float IdleResetTime { get; set; } = 0.5f;

		public float MovementTimer => _movementTimer;

		public float IdleTimer => _idleTimer;

		public bool IsMovementDetected => _isMovementDetected;

		public bool IsClickPending => _isClickPending;

		public bool IsSwitchingToMouse => _isSwitchingToMouse;

		public MouseDeadzoneStrategy()
		{
			_lastMousePosition = Input.mousePosition;
		}

		public void Update(ControllerType activeControllerType, Action onSwitchToMouse)
		{
			Vector2 vector = Input.mousePosition;
			float num = Vector2.Distance(vector, _lastMousePosition);
			_lastMousePosition = vector;
			if (HasMouseClick() && activeControllerType != ControllerType.Mouse && !_isSwitchingToMouse)
			{
				_isClickPending = true;
				_movementTimer = MovementDeadzoneTime;
				_idleTimer = 0f;
				_isSwitchingToMouse = true;
				onSwitchToMouse?.Invoke();
				_isSwitchingToMouse = false;
				_isClickPending = false;
				return;
			}
			if (num > 0.1f)
			{
				_movementTimer = Mathf.Min(_movementTimer + Time.unscaledDeltaTime, MovementDeadzoneTime);
				_idleTimer = 0f;
				_isMovementDetected = true;
			}
			else if (activeControllerType == ControllerType.Mouse)
			{
				_idleTimer += Time.unscaledDeltaTime;
			}
			else
			{
				_idleTimer += Time.unscaledDeltaTime;
				if (_idleTimer >= IdleResetTime)
				{
					_movementTimer = 0f;
					_isMovementDetected = false;
				}
			}
			if (_isMovementDetected && _movementTimer >= MovementDeadzoneTime && activeControllerType != ControllerType.Mouse && !_isSwitchingToMouse && !_isClickPending)
			{
				_isSwitchingToMouse = true;
				_idleTimer = 0f;
				onSwitchToMouse?.Invoke();
				_isSwitchingToMouse = false;
			}
		}

		public bool ShouldAllowControllerSwitch(Controller controller, Action onSwitchToMouse = null)
		{
			if (_isSwitchingToMouse && controller.type == ControllerType.Mouse)
			{
				return false;
			}
			if (controller.type == ControllerType.Mouse)
			{
				if (HasMouseClick())
				{
					_movementTimer = MovementDeadzoneTime;
					_idleTimer = 0f;
					_isSwitchingToMouse = true;
					onSwitchToMouse?.Invoke();
					_isSwitchingToMouse = false;
					return onSwitchToMouse == null;
				}
				if (_movementTimer >= MovementDeadzoneTime)
				{
					_idleTimer = 0f;
					_isSwitchingToMouse = true;
					onSwitchToMouse?.Invoke();
					_isSwitchingToMouse = false;
					return onSwitchToMouse == null;
				}
				return false;
			}
			ResetState();
			return true;
		}

		public void ResetState()
		{
			_movementTimer = 0f;
			_idleTimer = 0f;
			_isMovementDetected = false;
			_isSwitchingToMouse = false;
			_isClickPending = false;
		}

		private static bool HasMouseClick()
		{
			if (!Input.GetMouseButtonDown(0) && !Input.GetMouseButtonDown(1))
			{
				return Input.GetMouseButtonDown(2);
			}
			return true;
		}
	}
}
