using System;
using System.Threading;
using Animancer;
using AstralShift.Helpers;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class OvidSummonIdleModule : IdleStateModule
	{
		[SerializeField]
		private OvidSummonMover mover;

		[SerializeField]
		private float stopDistance = 2f;

		[SerializeField]
		private ClipTransition idleAnimation;

		[SerializeField]
		private ClipTransition birthAnimation;

		private bool _isCacoon = true;

		private CancellationTokenSource _cts;

		private Transform _playerTransform;

		public bool IsCacoon
		{
			get
			{
				return _isCacoon;
			}
			set
			{
				_isCacoon = value;
			}
		}

		protected float StopDistance => _aiBehaviour.WeaponBehaviour.SizeValue * stopDistance;

		public override void Init(SummonAIBehaviour behaviour, Action onComplete)
		{
			base.Init(behaviour, onComplete);
			if (_cts == null)
			{
				_cts = new CancellationTokenSource();
			}
			_playerTransform = GameDirector.Instance.Player.transform;
			mover.Init(behaviour);
		}

		public override void Enter()
		{
			if (_isCacoon)
			{
				PlayIdleAnimation();
			}
			else
			{
				Exit();
			}
		}

		public override async void Exit()
		{
			if (_isCacoon)
			{
				try
				{
					_isCacoon = false;
					await PlayBirthSequence();
				}
				catch (OperationCanceledException)
				{
				}
				catch (Exception exception)
				{
					Debug.LogException(exception);
				}
			}
			ExitInstant();
		}

		public void ExitInstant()
		{
			base.Exit();
		}

		private async UniTask PlayBirthSequence()
		{
			mover.StopCacoon();
			await PlayBirthAnimation();
			_aiBehaviour.WeaponBehaviour.SetLastAttackTime();
		}

		private void OnDestroy()
		{
			_cts?.Cancel();
			_cts?.Dispose();
			_cts = null;
		}

		public override void OnUpdate()
		{
			if (!(_playerTransform == null) && _isCacoon)
			{
				Vector2 vector = _aiBehaviour.Transform.position;
				Vector2 vector2 = _playerTransform.position;
				float distance = Vector2.Distance(vector, vector2);
				Vector2 normalized = (vector2 - vector).normalized;
				mover.MoveCacoon(normalized, distance, StopDistance);
			}
		}

		private void PlayIdleAnimation()
		{
			_aiBehaviour.Animancer.Play(idleAnimation);
		}

		private UniTask PlayBirthAnimation()
		{
			return AnimancerHelpers.AnimationTask(_aiBehaviour.Animancer, birthAnimation, 0, _cts.Token);
		}
	}
}
