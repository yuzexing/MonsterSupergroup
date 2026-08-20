using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Animancer;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Helpers;
using AstralShift.Helpers;
using Cysharp.Threading.Tasks;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class OvidSummonAttackModule : AttackStateModule
	{
		[SerializeField]
		private OvidSummonMover mover;

		[SerializeField]
		private BaseAttackHitBox hitBox;

		[SerializeField]
		private float minDetectionRadius = 5f;

		[SerializeField]
		private float maxDetectionRadius = 10f;

		[SerializeField]
		private float clusterSearchRadius = 4f;

		[SerializeField]
		private int framePartitioningCount = 4;

		[SerializeField]
		private int maxEnemiesToProcess = 100;

		[SerializeField]
		private float angleOffset = 180f;

		[SerializeField]
		private float aimSmoothing = 15f;

		[SerializeField]
		private float sweepAngle = 30f;

		[SerializeField]
		private CustomAnimationCurve sweepAccelerationCurve;

		[SerializeField]
		private float predictionLeadTime = 0.2f;

		[SerializeField]
		private float velocitySmoothing = 10f;

		[SerializeField]
		private ClipTransition attackEnterAnimation;

		[SerializeField]
		private ClipTransition attackLoopAnimation;

		[SerializeField]
		private ClipTransition attackExitAnimation;

		[SerializeField]
		private EventReference beamSound;

		private EventInstance attackSoundInstance;

		private List<BaseEnemyController> _tempTargetsList;

		private BaseEnemyController _currentTarget;

		private bool _targetIsBoss;

		private CancellationTokenSource _cts;

		private Transform _rotationPivot;

		private Vector2 _lastTargetPosition;

		private Vector2 _smoothedVelocity;

		public float MinDetectionRadius => _aiBehaviour.WeaponBehaviour.SizeValue * minDetectionRadius;

		public float MaxDetectionRadius => _aiBehaviour.WeaponBehaviour.SizeValue * maxDetectionRadius;

		public override void Init(SummonAIBehaviour behaviour, Action onComplete)
		{
			if (_cts == null)
			{
				_cts = new CancellationTokenSource();
			}
			base.Init(behaviour, onComplete);
			_rotationPivot = mover.GetRotationPivot();
		}

		public override void Enter()
		{
			_currentTarget = null;
			_smoothedVelocity = Vector2.zero;
			StartSequence(_cts.Token).Forget();
		}

		public override void Exit()
		{
			_cts?.Cancel();
			_cts?.Dispose();
			_cts = new CancellationTokenSource();
			_aiBehaviour.WeaponBehaviour.SetLastAttackTime();
			base.Exit();
		}

		public override void OnUpdate()
		{
		}

		private async UniTaskVoid StartSequence(CancellationToken token)
		{
			try
			{
				bool flag = await TryFindEnemyAsync(token);
				if (!token.IsCancellationRequested)
				{
					if (!flag)
					{
						Exit();
					}
					else
					{
						ExecuteSequence(token).Forget();
					}
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		private async UniTaskVoid ExecuteSequence(CancellationToken token)
		{
			_ = 3;
			try
			{
				_aiBehaviour.WeaponBehaviour.Attack();
				hitBox.Init(OnHit);
				if (_currentTarget != null)
				{
					_lastTargetPosition = _currentTarget.GetHurtBoxPosition();
				}
				attackSoundInstance = RuntimeManager.CreateInstance(beamSound);
				Vector3 position = base.transform.parent.position;
				position.z = 0f;
				attackSoundInstance.set3DAttributes(position.To3DAttributes());
				attackSoundInstance.start();
				bool isAnimating = true;
				await UniTask.WhenAll(PlayAnimation(attackEnterAnimation, token).ContinueWith(() => isAnimating = false), RotateTowardsTarget(aimSmoothing, token, () => isAnimating, !_targetIsBoss));
				_aiBehaviour.Animancer.Play(attackLoopAnimation);
				float durationValue = _aiBehaviour.WeaponBehaviour.DurationValue;
				if (_targetIsBoss)
				{
					await UpdateBeamTrack(durationValue, token);
				}
				else
				{
					float y = _rotationPivot.localEulerAngles.y;
					await UpdateBeamSweep(durationValue, y, token);
				}
				await PlayAnimation(attackExitAnimation, token);
				Exit();
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
			finally
			{
				if (attackSoundInstance.isValid())
				{
					attackSoundInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
					attackSoundInstance.release();
				}
			}
		}

		private async UniTask RotateTowardsTarget(float smoothing, CancellationToken token, Func<bool> condition, bool applySweepOffset)
		{
			while (condition() && !token.IsCancellationRequested && (bool)_currentTarget && _currentTarget.gameObject.activeInHierarchy)
			{
				Vector2 hurtBoxPosition = _currentTarget.GetHurtBoxPosition();
				float smoothDeltaTime = Time.smoothDeltaTime;
				Vector2 b = (hurtBoxPosition - _lastTargetPosition) / smoothDeltaTime;
				_smoothedVelocity = Vector2.Lerp(_smoothedVelocity, b, smoothDeltaTime * velocitySmoothing);
				_lastTargetPosition = hurtBoxPosition;
				Vector2 vector = hurtBoxPosition + _smoothedVelocity * predictionLeadTime - (Vector2)_aiBehaviour.Transform.position;
				float num = Mathf.Atan2(vector.x, vector.y) * 57.29578f + angleOffset;
				if (applySweepOffset)
				{
					num -= sweepAngle / 2f;
				}
				float y = Mathf.LerpAngle(_rotationPivot.localEulerAngles.y, num, Time.smoothDeltaTime * smoothing);
				_rotationPivot.localRotation = Quaternion.Euler(_rotationPivot.localEulerAngles.x, y, 0f);
				await UniTask.Yield(PlayerLoopTiming.Update, token);
			}
		}

		private async UniTask UpdateBeamSweep(float duration, float startYAngle, CancellationToken token)
		{
			float endYAngle = startYAngle + sweepAngle;
			float elapsed = 0f;
			while (elapsed < duration && !token.IsCancellationRequested && !(_currentTarget == null) && _currentTarget.gameObject.activeInHierarchy)
			{
				elapsed += Time.smoothDeltaTime;
				float t = Mathf.Clamp01(elapsed / duration);
				float t2 = sweepAccelerationCurve.EasePercentage(t);
				float y = Mathf.LerpAngle(startYAngle, endYAngle, t2);
				_rotationPivot.localRotation = Quaternion.Euler(_rotationPivot.localEulerAngles.x, y, 0f);
				await UniTask.Yield(PlayerLoopTiming.Update, token);
			}
		}

		private async UniTask UpdateBeamTrack(float duration, CancellationToken token)
		{
			if ((bool)_currentTarget)
			{
				Vector2 vector = _currentTarget.GetHurtBoxPosition() + _smoothedVelocity * predictionLeadTime - (Vector2)_aiBehaviour.Transform.position;
				float targetYAngle = Mathf.Atan2(vector.x, vector.y) * 57.29578f + angleOffset;
				float elapsed = 0f;
				while (elapsed < duration && !token.IsCancellationRequested)
				{
					elapsed += Time.smoothDeltaTime;
					float y = Mathf.LerpAngle(_rotationPivot.localEulerAngles.y, targetYAngle, Time.smoothDeltaTime * aimSmoothing);
					_rotationPivot.localRotation = Quaternion.Euler(_rotationPivot.localEulerAngles.x, y, 0f);
					await UniTask.Yield(PlayerLoopTiming.Update, token);
				}
			}
		}

		private async UniTask<bool> TryFindEnemyAsync(CancellationToken token)
		{
			if (_tempTargetsList == null)
			{
				_tempTargetsList = new List<BaseEnemyController>();
			}
			AIHelpers.FindEnemiesInCircleRangeNonAlloc(_aiBehaviour.Transform.position, MinDetectionRadius, MaxDetectionRadius, _tempTargetsList);
			if (_tempTargetsList.Count == 0)
			{
				_targetIsBoss = false;
				return false;
			}
			if (_tempTargetsList.Count == 1)
			{
				_currentTarget = _tempTargetsList[0];
				_targetIsBoss = _tempTargetsList[0].ID == -1;
				return true;
			}
			if (_tempTargetsList.Count > maxEnemiesToProcess)
			{
				_tempTargetsList = _tempTargetsList.OrderBy((BaseEnemyController element) => (element.GetHurtBoxPosition() - (Vector2)_aiBehaviour.Transform.position).sqrMagnitude).Take(maxEnemiesToProcess).ToList();
			}
			int totalEnemies = _tempTargetsList.Count;
			List<(BaseEnemyController enemy, int score)> enemyDensityScores = new List<(BaseEnemyController, int)>();
			int chunkSize = Mathf.Max(1, Mathf.CeilToInt((float)totalEnemies / (float)framePartitioningCount));
			for (int i = 0; i < totalEnemies; i++)
			{
				BaseEnemyController enemy = _tempTargetsList[i];
				int item = _tempTargetsList.Count((BaseEnemyController element) => element != enemy && Vector2.Distance(enemy.GetHurtBoxPosition(), element.GetHurtBoxPosition()) <= clusterSearchRadius);
				enemyDensityScores.Add((enemy, item));
				if ((i + 1) % chunkSize == 0 && i < totalEnemies - 1)
				{
					await UniTask.Yield(PlayerLoopTiming.Update, token);
				}
			}
			if (enemyDensityScores.Count == 0)
			{
				_targetIsBoss = false;
				return false;
			}
			_currentTarget = enemyDensityScores.OrderByDescending(((BaseEnemyController enemy, int score) x) => x.score).First().enemy;
			_targetIsBoss = _currentTarget.ID == -1;
			return true;
		}

		private void OnHit(IDamageable damageable)
		{
			_aiBehaviour.WeaponBehaviour.Damage(hitBox.transform.position, damageable);
		}

		private UniTask PlayAnimation(ClipTransition clip, CancellationToken token)
		{
			return AnimancerHelpers.AnimationTask(_aiBehaviour.Animancer, clip, 0, token);
		}
	}
}
