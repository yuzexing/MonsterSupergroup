using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Helpers;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class OvidSummonPositioningModule : PositioningStateModule
	{
		[SerializeField]
		private OvidSummonMover mover;

		[SerializeField]
		private float stopDistance = 4f;

		[Tooltip("The distance Ovid wants to be from the target before firing.")]
		[SerializeField]
		private float optimalAttackDistance = 6f;

		[SerializeField]
		private float minDetectionRadius = 5f;

		[SerializeField]
		private float maxDetectionRadius = 15f;

		private Transform _playerTransform;

		private BaseEnemyController _potentialTarget;

		private List<BaseEnemyController> _tempTargetsList;

		private const float OptimalAttackDistanceReferenceTolerance = 0.5f;

		private float StopDistance => _aiBehaviour.WeaponBehaviour.SizeValue * stopDistance;

		private float OptimalAttackDistance => _aiBehaviour.WeaponBehaviour.SizeValue * optimalAttackDistance;

		private float MinDetectionRadius => _aiBehaviour.WeaponBehaviour.SizeValue * minDetectionRadius;

		private float MaxDetectionRadius => _aiBehaviour.WeaponBehaviour.SizeValue * maxDetectionRadius;

		public override void Init(SummonAIBehaviour behaviour, Action onComplete)
		{
			base.Init(behaviour, onComplete);
			mover.Init(behaviour);
			_playerTransform = GameDirector.Instance.Player.transform;
		}

		public override void Enter()
		{
			_potentialTarget = null;
		}

		public override void Exit()
		{
			mover.Stop(immediately: true);
			mover.UpdateAnimation();
			base.Exit();
		}

		public override void OnUpdate()
		{
			if (!(_playerTransform == null))
			{
				Vector2 vector = _aiBehaviour.Transform.position;
				_potentialTarget = AIHelpers.FindClosestEnemyInCircleRange(vector, MinDetectionRadius, MaxDetectionRadius);
				if (_potentialTarget != null && _aiBehaviour.WeaponBehaviour.CheckCooldown())
				{
					HandleOptimalCombatPositioning(vector);
				}
				else
				{
					HandlePlayerFollowing(vector);
				}
			}
		}

		private void HandleOptimalCombatPositioning(Vector2 currentPos)
		{
			Vector2 hurtBoxPosition = _potentialTarget.GetHurtBoxPosition();
			float num = Vector2.Distance(currentPos, hurtBoxPosition);
			if (num <= OptimalAttackDistance + 0.5f)
			{
				Exit();
				return;
			}
			Vector2 normalized = (hurtBoxPosition - currentPos).normalized;
			mover.Move(normalized, num, OptimalAttackDistance);
			mover.UpdateAnimation();
		}

		private void HandlePlayerFollowing(Vector2 currentPos)
		{
			Vector2 vector = _playerTransform.position;
			float num = Vector2.Distance(currentPos, vector);
			if (num > StopDistance)
			{
				Vector2 normalized = (vector - currentPos).normalized;
				mover.Move(normalized, num, StopDistance);
				mover.UpdateAnimation();
			}
			else
			{
				mover.Stop();
				mover.UpdateAnimation();
			}
		}
	}
}
