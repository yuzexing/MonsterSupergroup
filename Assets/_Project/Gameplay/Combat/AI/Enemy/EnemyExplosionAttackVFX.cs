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

        private float sampledVisualTime;
        private bool samplingTimeline, timelineEmissionStopped;

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
			TriggerVisual();
			SetDamageEnabled(true);
		}

        public void SetDamageEnabled(bool enabled)
        { if (colliders.activeSelf != enabled) colliders.SetActive(enabled); }

        public void TriggerVisual()
        {
			sampledVisualTime = 0; samplingTimeline = timelineEmissionStopped = false;
			particleSystem.Play();
			SetDamageEnabled(false);
			if (_particlesLifetimeCheck != null)
			{
				StopCoroutine(_particlesLifetimeCheck);
			}
		}

        // Seek emission and its tail separately. A late Recovery must not emit throughout the skipped tail.
        public void SampleCombatTimeline(float elapsed, float activeDuration)
        {
            if (!samplingTimeline)
            {
                particleSystem.Simulate(0, true, true, true);
                samplingTimeline = true;
            }
            elapsed = Mathf.Max(sampledVisualTime, elapsed);
            float emissionTime = Mathf.Min(elapsed, activeDuration);
            if (emissionTime > sampledVisualTime)
                particleSystem.Simulate(emissionTime - sampledVisualTime, true, false, true);
            if (elapsed >= activeDuration && !timelineEmissionStopped)
            {
                Stop();
                timelineEmissionStopped = true;
            }
            float tailStart = Mathf.Max(sampledVisualTime, activeDuration);
            if (elapsed > tailStart) particleSystem.Simulate(elapsed - tailStart, true, false, true);
            particleSystem.Pause(true);
            sampledVisualTime = elapsed;
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
