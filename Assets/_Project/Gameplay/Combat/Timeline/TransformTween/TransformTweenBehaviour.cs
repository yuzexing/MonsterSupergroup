using System;
using AstralShift.HellMaiden.Characters;
using UnityEngine;
using UnityEngine.Playables;

namespace AstralShift.HellMaiden.Timeline.TransformTween
{
	[Serializable]
	public class TransformTweenBehaviour : EmoteBehaviour
	{
		public enum TweenType
		{
			Linear = 0,
			Deceleration = 1,
			Harmonic = 2,
			Custom = 3
		}

		public Transform startLocation;

		public Transform endLocation;

		public bool tweenPosition = true;

		public bool tweenRotation = true;

		public TweenType tweenType;

		public AnimationCurve customCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

		public Vector3 startingPosition;

		public Quaternion startingRotation = Quaternion.identity;

		private AnimationCurve m_LinearCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

		private AnimationCurve m_DecelerationCurve = new AnimationCurve(new Keyframe(0f, 0f, -MathF.PI / 2f, MathF.PI / 2f), new Keyframe(1f, 1f, 0f, 0f));

		private AnimationCurve m_HarmonicCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

		private const float k_RightAngleInRads = MathF.PI / 2f;

		public bool isMoonwalking;

		private Transform binding;

		public override void PrepareFrame(Playable playable, FrameData info)
		{
			if ((bool)startLocation)
			{
				startingPosition = startLocation.position;
				startingRotation = startLocation.rotation;
			}
		}

		public override void ProcessFrame(Playable playable, FrameData info, object playerData)
		{
			binding = playerData as Transform;
			base.ProcessFrame(playable, info, playerData);
		}

		public float EvaluateCurrentCurve(float time)
		{
			if (tweenType == TweenType.Custom && !IsCustomCurveNormalised())
			{
				Debug.LogError("Custom Curve is not normalised.  Curve must start at 0,0 and end at 1,1.");
				return 0f;
			}
			return tweenType switch
			{
				TweenType.Linear => m_LinearCurve.Evaluate(time), 
				TweenType.Deceleration => m_DecelerationCurve.Evaluate(time), 
				TweenType.Harmonic => m_HarmonicCurve.Evaluate(time), 
				_ => customCurve.Evaluate(time), 
			};
		}

		private bool IsCustomCurveNormalised()
		{
			if (!Mathf.Approximately(customCurve[0].time, 0f))
			{
				return false;
			}
			if (!Mathf.Approximately(customCurve[0].value, 0f))
			{
				return false;
			}
			if (!Mathf.Approximately(customCurve[customCurve.length - 1].time, 1f))
			{
				return false;
			}
			return Mathf.Approximately(customCurve[customCurve.length - 1].value, 1f);
		}

		public override void OnBehaviourPause(Playable playable, FrameData info)
		{
			if (Application.isPlaying)
			{
				double duration = playable.GetDuration();
				double time = playable.GetTime();
				double num = time + (double)info.deltaTime;
				if ((info.effectivePlayState == PlayState.Paused && num > duration) || Mathf.Approximately((float)time, (float)duration))
				{
					SnapToTarget();
					OnEnd();
				}
			}
		}

		private void SnapToTarget()
		{
			binding.transform.position = endLocation.position;
			binding.GetComponent<CharacterMovement>().StopMovement();
			Debug.Log("SnapToTarget " + binding.transform.position);
		}
	}
}
