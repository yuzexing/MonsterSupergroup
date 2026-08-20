using System;
using AstralShift.FSM;
using AstralShift.HellMaiden.Player;
using AstralShift.Managers;
using UnityEngine;

namespace AstralShift.HellMaiden.Characters
{
	public class CharacterMovement : MonoBehaviour, IPausable
	{
		public Rigidbody2D body;

		public CharacterAnimator animator;

		public SpriteRenderer spriteRenderer;

		[SerializeField]
		private float moveSpeed;

		protected Vector2 _currentInputDirection;

		protected Vector2 _facingDirection = Vector2.zero;

		protected StateMachine _stateMachine;

		protected State Moving;

		public virtual float MoveSpeed => moveSpeed;

		public Vector2 CurrentInputDirection => _currentInputDirection;

		public Vector2 FacingDirection
		{
			get
			{
				return _facingDirection;
			}
			set
			{
				_facingDirection = value;
			}
		}

		public Vector2 Velocity => body.linearVelocity;

		public Vector3 CurrentPosition => base.transform.position;

		public StateMachine StateMachine => _stateMachine;

		public virtual void Awake()
		{
			_stateMachine = new StateMachine("CharacterMovement");
			Moving = new State("Moving");
			State moving = Moving;
			moving.onFixedUpdateTick = (Action)Delegate.Combine(moving.onFixedUpdateTick, new Action(OnFixedUpdateMoving));
			State moving2 = Moving;
			moving2.onLateUpdateTick = (Action)Delegate.Combine(moving2.onLateUpdateTick, new Action(OnLateUpdateMoving));
			_stateMachine.SetInitialState(Moving);
		}

		protected virtual void Start()
		{
			SubscribeGameEvents();
		}

		private void Update()
		{
			_stateMachine.UpdateTick();
		}

		private void FixedUpdate()
		{
			_stateMachine.FixedUpdateTick();
		}

		private void LateUpdate()
		{
			_stateMachine.LateUpdateTick();
		}

		private void SubscribeGameEvents()
		{
			((IPausable)this).Subscribe();
		}

		private void UnSubscribeGameEvents()
		{
			((IPausable)this).UnSubscribe();
		}

		public virtual void SetDirection(Vector2 value)
		{
			_currentInputDirection = value;
			_facingDirection.x = ((_currentInputDirection.x == 0f) ? FacingDirection.x : _currentInputDirection.x);
			_facingDirection.y = ((_currentInputDirection.y == 0f) ? FacingDirection.y : _currentInputDirection.y);
		}

		public virtual void SetDirectionImmediate(Vector2 value)
		{
			_currentInputDirection = value;
			_facingDirection = _currentInputDirection;
		}

		private void OnFixedUpdateMoving()
		{
			body.linearVelocity = _currentInputDirection.normalized * moveSpeed;
		}

		protected virtual void OnLateUpdateMoving()
		{
			animator.Movement(_currentInputDirection.magnitude, FacingDirection.x, FacingDirection.y);
		}

		protected virtual void OnDestroy()
		{
			UnSubscribeGameEvents();
		}

		public virtual void OnPausePausables()
		{
		}

		public virtual void OnResumePausables()
		{
		}

		public virtual void StopMovement()
		{
			_currentInputDirection = Vector2.zero;
		}
	}
}
