using System;
using System.Collections;
using MonsterSupergroup.GAS;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class AttackHitParticleEffect : BaseAttackHitEffect
	{
		public ParticleSystem system;

		private Action _onEnd;

		private Action _playOnEnableAction;

		private Coroutine _checkDeathCoroutine;
		private bool _stopping;

		private const float CheckTimeStep = 0.33f;

		[SerializeField]
		[Tooltip("If set to 0 or less it won't be considered.")]
		private float timeToLive;

		private float timer;

		protected virtual void OnEnable()
		{
			Action play = _playOnEnableAction;
			_playOnEnableAction = null;
			play?.Invoke();
		}

		protected virtual void OnDisable()
		{
			Complete();
		}

		protected virtual void OnDestroy() => Complete();

		public override void Init(WeaponBehaviour behaviour)
		{
			if ((bool)progressionScaler)
			{
				progressionScaler.Apply(behaviour);
			}
		}

		public override void Init(WeaponBehaviour behaviour, AttackSnapshot attack)
		{
			if ((bool)progressionScaler)
			{
				progressionScaler.Apply(attack.Stats);
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
			if (base.gameObject.activeSelf)
			{
				Play(onHit, onEnd);
			}
			else
			{
				PrepareToPlay(onEnd, onHit);
			}
		}

		private void PrepareToPlay(Action onEnd, Action<IDamageable> onHit = null)
		{
			_onEnd = onEnd;
			if ((bool)hitbox)
			{
				hitbox.Init(onHit);
			}
			_playOnEnableAction = delegate
			{
				Play(onEnd);
			};
		}

		public override void Play(Action onEnd)
		{
			_onEnd = onEnd;
			_stopping = false;
			if (system == null)
			{
				Complete();
				return;
			}
			system.Play(withChildren: true);
			hitbox?.Toggle(state: true);
			if (timeToLive > 0f)
			{
				if (_checkDeathCoroutine != null)
				{
					StopCoroutine(_checkDeathCoroutine);
				}
				if (base.gameObject.activeSelf)
				{
					_checkDeathCoroutine = StartCoroutine(RunTimerDeathCheck());
				}
				else
				{
					Debug.LogWarning("Cannot start particle lifetime check for " + base.gameObject.name + " because it is inactive.");
				}
			}
			else
			{
				RunParticleSystemDeathCheck();
			}
		}

		public override void Play(Action<IDamageable> onHit, Action onEnd)
		{
			if ((bool)hitbox)
			{
				hitbox.Init(onHit);
			}
			Play(onEnd);
		}

		private IEnumerator RunTimerDeathCheck()
		{
			timer = 0f;
			while (timer < timeToLive)
			{
				timer += Time.deltaTime;
				yield return null;
			}
			_checkDeathCoroutine = null;
			Complete();
		}

		private void RunParticleSystemDeathCheck()
		{
			if (_checkDeathCoroutine != null)
			{
				StopCoroutine(_checkDeathCoroutine);
			}
			if (base.gameObject.activeSelf)
			{
				_checkDeathCoroutine = StartCoroutine(CheckParticleSystemDeath());
			}
			else
			{
				Debug.LogWarning("Cannot start particle lifetime check for " + base.gameObject.name + " because it is inactive.");
			}
		}

		private IEnumerator CheckParticleSystemDeath()
		{
			WaitForSeconds timeStepYield = new WaitForSeconds(0.33f);
			yield return timeStepYield;
			while (system != null && system.IsAlive(withChildren: true))
			{
				yield return timeStepYield;
			}
			_checkDeathCoroutine = null;
			Complete();
		}

		public override void Stop()
		{
			if (_stopping || _onEnd == null) return;
			_stopping = true;
			if (system == null || !isActiveAndEnabled)
			{
				Complete();
				return;
			}
			system.Stop(withChildren: true);
			RunParticleSystemDeathCheck();
		}

		private void Complete()
		{
			// Clear before invoking: the callback can return this object to a pool,
			// recursively disable it, or spawn a new effect on the same owner.
			Action onEnd = _onEnd;
			_onEnd = null;
			_playOnEnableAction = null;
			_stopping = true;
			if (_checkDeathCoroutine != null)
			{
				StopCoroutine(_checkDeathCoroutine);
				_checkDeathCoroutine = null;
			}
			if (system != null)
				system.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmittingAndClear);
			hitbox?.ClearCallbacks();
			hitbox?.Toggle(state: false);
			onEnd?.Invoke();
		}
	}
}
