using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class EnemyExplosionAttackVFX : EnemyAttackPrefab
	{
		public ParticleSystem particleSystem;

		public GameObject colliders;

		private List<ParticleSystem> _allSystems;

		private Action _onEnd;

		private Coroutine _particlesLifetimeCheck;

		private void Awake()
		{
			Init();
		}

		public void Init()
		{
			colliders.SetActive(value: false);
			particleSystem.Clear(withChildren: true);
		}

		public void Trigger(Action onEnd)
		{
			_onEnd = onEnd;
			particleSystem.Play();
			colliders.SetActive(value: true);
			if (_particlesLifetimeCheck != null)
			{
				StopCoroutine(_particlesLifetimeCheck);
			}
		}

		public void Stop()
		{
			particleSystem.Stop();
			colliders.SetActive(value: false);
		}

		private IEnumerator CheckIfParticlesStopped()
		{
			WaitForSeconds waitInstance = new WaitForSeconds(0.3f);
			while (particleSystem.IsAlive(withChildren: true))
			{
				yield return waitInstance;
			}
			_particlesLifetimeCheck = null;
			OnEnd();
		}

		private void OnEnd()
		{
			_onEnd?.Invoke();
		}
	}
}
