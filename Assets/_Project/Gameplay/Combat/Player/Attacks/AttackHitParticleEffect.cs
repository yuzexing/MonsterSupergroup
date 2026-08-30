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

		private const float CheckTimeStep = 0.33f;

		[SerializeField]
		[Tooltip("If set to 0 or less it won't be considered.")]
		private float timeToLive;

		private float timer;

		protected virtual void OnEnable()
		{
			_playOnEnableAction?.Invoke();
		}

		protected virtual void OnDisable()
		{
			Cleanup(killSystems: true);
			hitbox?.Toggle(state: false);
		}

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
			_onEnd = onEnd;
			if (system == null)
			{
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
					_checkDeathCoroutine = StartCoroutine(RunTimerDeathCheck(_onEnd));
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

		private IEnumerator RunTimerDeathCheck(Action onEnd)
		{
			timer = 0f;
			while (timer < timeToLive)
			{
				timer += Time.deltaTime;
				yield return null;
			}
			_checkDeathCoroutine = null;
			onEnd?.Invoke();
		}

		private void RunParticleSystemDeathCheck()
		{
			if (_checkDeathCoroutine != null)
			{
				StopCoroutine(_checkDeathCoroutine);
			}
			if (base.gameObject.activeSelf)
			{
				_checkDeathCoroutine = StartCoroutine(CheckParticleSystemDeath(_onEnd));
			}
			else
			{
				Debug.LogWarning("Cannot start particle lifetime check for " + base.gameObject.name + " because it is inactive.");
			}
		}

		private IEnumerator CheckParticleSystemDeath(Action onEnd)
		{
			WaitForSeconds timeStepYield = new WaitForSeconds(0.33f);
			yield return timeStepYield;
			while (system.IsAlive(withChildren: true))
			{
				yield return timeStepYield;
			}
			_checkDeathCoroutine = null;
			onEnd?.Invoke();
		}

		public override void Stop()
		{
			system.Stop(withChildren: true);
			RunParticleSystemDeathCheck();
		}

		private void Cleanup(bool killSystems)
		{
			if (_checkDeathCoroutine != null)
			{
				StopCoroutine(_checkDeathCoroutine);
				_checkDeathCoroutine = null;
				_onEnd?.Invoke();
				_onEnd = null;
			}
			if (killSystems)
			{
				system.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmittingAndClear);
			}
			else
			{
				system.Stop(withChildren: true);
			}
			hitbox?.ClearCallbacks();
		}
	}
}
