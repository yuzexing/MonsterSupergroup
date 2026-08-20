using System;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class ChainLightningHitEffect : BaseAttackHitEffect
	{
		private Action _onEnd;

		private Action _playOnEnableAction;

		private Coroutine _checkParticleSystemDeathCoroutine;

		[SerializeField]
		private ChainLightningController _controller;

		public override void Init(WeaponBehaviour behaviour)
		{
			if ((bool)progressionScaler)
			{
				progressionScaler.Apply(behaviour);
			}
		}

		public override void PlayOnEnable(Action onEnd)
		{
			if (base.gameObject.activeSelf)
			{
				Play(onEnd);
			}
			else
			{
				PrepareToPlay(onEnd);
			}
		}

		public override void PlayOnEnable(Action<IDamageable> onHit, Action onEnd)
		{
		}

		private void PrepareToPlay(Action onEnd, Action<IDamageable> onHit = null)
		{
			if ((bool)hitbox)
			{
				hitbox.Init(onHit);
			}
			_playOnEnableAction = delegate
			{
				Play(onEnd);
				_playOnEnableAction = null;
			};
		}

		public override void Play(Action onEnd)
		{
			_controller.Play(onEnd);
		}

		public override void Play(Action<IDamageable> onHit, Action onEnd)
		{
		}

		public override void Stop()
		{
		}

		public void SetTesla(Transform startPoint, Transform endPoint)
		{
			_controller.InitializeEffect(startPoint, endPoint);
		}
	}
}
