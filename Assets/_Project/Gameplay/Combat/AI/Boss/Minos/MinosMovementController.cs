using System;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.AI.Enemy.Boss;
using AstralShift.QTI.Helpers.Attributes;
using DG.Tweening;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Boss.Minos
{
	public class MinosMovementController : BaseEnemyMovement
	{
		protected float reachedDestinationThreshold = 0.05f;

		public Ease movementEase = Ease.OutQuint;

		public BossAnimator animator;

		private Tween moveTween;

		[SerializeField]
		private Transform centerPosition;

		[SerializeField]
		private bool curvedPath;

		[SerializeField]
		[ConditionalHide("curvedPath", true)]
		private float curvedPathHeight = 2f;

		[SerializeField]
		[ConditionalHide("curvedPath", true)]
		[Range(-1f, 1f)]
		private int curveDirection = 1;

		public Vector3 Position => _transform.position;

		private event Action OnTargetReachedCallback;

		private void Start()
		{
			enemyController.stats.OnSpeedChanged += SetMovementSpeed;
		}

		public void SetDestination(Vector3 destination, Action onEnd = null, float speed = 30f, Shooter shooter = null)
		{
			Destination = destination;
			this.OnTargetReachedCallback = onEnd;
			if (Destination == base.transform.position)
			{
				OnTargetReached();
				return;
			}
			float duration = Vector2.Distance(_transform.position, destination) / speed;
			Debug.Log("SetDestination, duration = " + duration);
			animator.Run(destination.x - _transform.position.x, destination.y - _transform.position.y);
			if (moveTween != null)
			{
				moveTween.Pause();
			}
			if (curvedPath)
			{
				Vector3 vector = (Position + destination) / 2f + Vector3.up * ((float)curveDirection * curvedPathHeight);
				moveTween = _transform.DOPath(new Vector3[3] { Position, vector, destination }, duration, PathType.CatmullRom).SetEase(movementEase).OnComplete(OnTargetReached);
			}
			else
			{
				moveTween = _transform.DOMove(destination, duration).SetEase(movementEase).OnComplete(OnTargetReached);
			}
			moveTween.timeScale = enemyController.stats.SpeedMultiplier;
			moveTween.Restart();
			if (shooter != null)
			{
				shooter.ShootBullets();
			}
		}

		public void SetPath(Vector3[] path, Action onEnd = null, float speed = 30f, Shooter shooter = null)
		{
			this.OnTargetReachedCallback = onEnd;
			if (path == null || path.Length == 0)
			{
				OnTargetReached();
				return;
			}
			animator.Run(0f, 0f);
			if (moveTween != null)
			{
				moveTween.Pause();
			}
			moveTween = _transform.DOPath(path, speed, PathType.CatmullRom).SetEase(movementEase).OnComplete(OnTargetReached);
			moveTween.timeScale = enemyController.stats.SpeedMultiplier;
			moveTween.Restart();
			if (shooter != null)
			{
				shooter.ShootBullets();
			}
		}

		public void MoveToCenter(float moveSpeed)
		{
			SetDestination(centerPosition.position, null, moveSpeed);
		}

		private void SetMovementSpeed()
		{
			if (moveTween != null)
			{
				moveTween.timeScale = enemyController.stats.SpeedMultiplier;
			}
		}

		public void OnTargetReached()
		{
			this.OnTargetReachedCallback?.Invoke();
			this.OnTargetReachedCallback = null;
			_direction = Vector2.zero;
			animator.Idle(0f, 0f);
		}

		public override void MovementUpdate()
		{
			Debug.Log("MovementUpdate");
		}

		public override void StopMovement(float timeout = 0f)
		{
			moveTween.Kill();
		}
	}
}
