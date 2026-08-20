using System;
using System.Collections;
using AstralShift.QTI.Helpers;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Boss.Scarmiglione
{
	public class FireCircleOfDeath : AnimatedBossAttack
	{
		private float angle;

		public float startingRotationSpeed = 1f;

		private float rotationSpeed = 1f;

		public float ttl = 10f;

		public float speedIncrement = 0.2f;

		public Action onEnd;

		[Header("Audio")]
		[SerializeField]
		private EventReference fireLoopEvent;

		[SerializeField]
		private float soundVelocityPeak = 1f;

		[SerializeField]
		private float phaseRampDuration = 1f;

		private EventInstance _instance;

		private Quaternion _previousRotation;

		private void OnEnable()
		{
			angle = 0f;
			rotationSpeed = startingRotationSpeed;
			RunInAnimation(RunLoopAnimation);
			animancer.Evaluate();
			CreateAndPlayInstance();
			_previousRotation = rotationTransform.rotation;
			SetPhase(0f);
			StartCoroutine(Wait.SetTimeout(ttl, Despawn));
		}

		private void LateUpdate()
		{
			UpdateAudioVelocity();
		}

		private void UpdateAudioVelocity()
		{
			if (_instance.isValid())
			{
				Quaternion rotation = rotationTransform.rotation;
				float value = Quaternion.Angle(_previousRotation, rotation) / Time.deltaTime;
				_previousRotation = rotation;
				float value2 = Mathf.Clamp(value, 0f, soundVelocityPeak) / soundVelocityPeak;
				_instance.setParameterByName("Velocity", value2);
			}
		}

		private void SetPhase(float value)
		{
			if (_instance.isValid())
			{
				_instance.setParameterByName("Phase", value);
			}
		}

		private IEnumerator RampPhase()
		{
			float t = 0f;
			while (t < phaseRampDuration)
			{
				t += Time.deltaTime;
				SetPhase(Mathf.Clamp01(t / phaseRampDuration));
				yield return null;
			}
			SetPhase(1f);
		}

		public void Despawn()
		{
			StopAllCoroutines();
			onEnd = (Action)Delegate.Combine(onEnd, (Action)delegate
			{
				base.gameObject.SetActive(value: false);
			});
			RunOutAnimation(onEnd);
			StartCoroutine(RampPhase());
		}

		private void CreateAndPlayInstance()
		{
			if (!fireLoopEvent.IsNull)
			{
				_instance = RuntimeManager.CreateInstance(fireLoopEvent);
				_instance.start();
			}
		}

		private void OnDisable()
		{
			ReleaseInstance();
		}

		private void ReleaseInstance()
		{
			if (_instance.isValid())
			{
				_instance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
				_instance.release();
				_instance.clearHandle();
			}
		}
	}
}
