using System;
using System.Collections;
using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Boss.Scarmiglione
{
	public class FireHedges : AnimatedBossAttack
	{
		public float speed = 2f;

		public Vector3 direction;

		public Action onDespawn;

		[SerializeField]
		private float despawnTime = 5f;

		public List<AnimatedBossAttack> waves = new List<AnimatedBossAttack>();

		public float wavesCadence = 1f;

		[Header("Trail Sound")]
		[SerializeField]
		protected EventReference fireWaveSound;

		private EventInstance _fireWaveSoundInstance;

		private void OnEnable()
		{
			foreach (AnimatedBossAttack wave in waves)
			{
				wave.gameObject.SetActive(value: false);
			}
			if (!fireWaveSound.IsNull)
			{
				_fireWaveSoundInstance = RuntimeManager.CreateInstance(fireWaveSound);
			}
			StartCoroutine(SpawnAnimatedWaves());
		}

		private void Start()
		{
			if (!fireWaveSound.IsNull)
			{
				_fireWaveSoundInstance.set3DAttributes(base.transform.To3DAttributes());
				_fireWaveSoundInstance.start();
			}
		}

		private void FixedUpdate()
		{
			base.transform.position += direction * speed;
		}

		private void OnDisable()
		{
			CleanUpSound();
		}

		private void OnDestroy()
		{
			CleanUpSound();
		}

		private IEnumerator SpawnAnimatedWaves()
		{
			WaitForSeconds wait = new WaitForSeconds(wavesCadence);
			for (int index = 0; index < waves.Count; index++)
			{
				AnimatedBossAttack animatedBossAttack = waves[index];
				animatedBossAttack.gameObject.SetActive(value: true);
				animatedBossAttack.RunInAnimation(animatedBossAttack.RunLoopAnimation);
				Action onEnd = ((index == 0) ? onDespawn : new Action(CleanUpSound));
				StartCoroutine(Despawn(animatedBossAttack, onEnd));
				if (index % 2 != 0)
				{
					yield return wait;
				}
			}
		}

		private IEnumerator Despawn(AnimatedBossAttack wave, Action onEnd)
		{
			yield return new WaitForSeconds(despawnTime);
			wave.RunOutAnimation(onEnd);
		}

		private void CleanUpSound()
		{
			if (_fireWaveSoundInstance.isValid())
			{
				_fireWaveSoundInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
				_fireWaveSoundInstance.release();
				_fireWaveSoundInstance = default(EventInstance);
			}
		}
	}
}
