using System;
using AstralShift.HellMaiden.Timeline.TransformTween;
using UnityEngine;
using UnityEngine.Playables;

namespace AstralShift.HellMaiden.Timeline
{
	public class TransformTweenMixerBehaviour : PlayableBehaviour
	{
		private bool m_FirstFrameHappened;

		protected Vector2 blendedPosition;

		public override void ProcessFrame(Playable playable, FrameData info, object playerData)
		{
			Transform transform = playerData as Transform;
			if (transform == null)
			{
				return;
			}
			Vector2 vector = transform.position;
			Quaternion rotation = transform.rotation;
			int inputCount = playable.GetInputCount();
			float num = 0f;
			float num2 = 0f;
			blendedPosition = Vector3.zero;
			Quaternion quaternion = new Quaternion(0f, 0f, 0f, 0f);
			for (int i = 0; i < inputCount; i++)
			{
				TransformTweenBehaviour transformTweenBehaviour = null;
				ScriptPlayable<TransformTweenBehaviour> scriptPlayable = default(ScriptPlayable<TransformTweenBehaviour>);
				try
				{
					scriptPlayable = (ScriptPlayable<TransformTweenBehaviour>)playable.GetInput(i);
					transformTweenBehaviour = scriptPlayable.GetBehaviour();
				}
				catch (Exception)
				{
					continue;
				}
				if (transformTweenBehaviour.endLocation == null)
				{
					continue;
				}
				float inputWeight = playable.GetInputWeight(i);
				if (!m_FirstFrameHappened && !transformTweenBehaviour.startLocation)
				{
					transformTweenBehaviour.startingPosition = vector;
					transformTweenBehaviour.startingRotation = rotation;
				}
				float time = (float)(scriptPlayable.GetTime() / scriptPlayable.GetDuration());
				float t = transformTweenBehaviour.EvaluateCurrentCurve(time);
				if (transformTweenBehaviour.tweenPosition)
				{
					num += inputWeight;
					blendedPosition += Vector2.Lerp(transformTweenBehaviour.startingPosition, transformTweenBehaviour.endLocation.position, t) * inputWeight;
				}
				if (transformTweenBehaviour.tweenRotation)
				{
					num2 += inputWeight;
					Quaternion rotation2 = Quaternion.Lerp(transformTweenBehaviour.startingRotation, transformTweenBehaviour.endLocation.rotation, t);
					rotation2 = NormalizeQuaternion(rotation2);
					if (Quaternion.Dot(quaternion, rotation2) < 0f)
					{
						rotation2 = ScaleQuaternion(rotation2, -1f);
					}
					rotation2 = ScaleQuaternion(rotation2, inputWeight);
					quaternion = AddQuaternions(quaternion, rotation2);
				}
			}
			blendedPosition += vector * (1f - num);
			Quaternion second = ScaleQuaternion(rotation, 1f - num2);
			quaternion = AddQuaternions(quaternion, second);
			transform.position = new Vector3(blendedPosition.x, blendedPosition.y, transform.transform.position.z);
			transform.rotation = quaternion;
			m_FirstFrameHappened = true;
		}

		public override void OnPlayableDestroy(Playable playable)
		{
			m_FirstFrameHappened = false;
		}

		private static Quaternion AddQuaternions(Quaternion first, Quaternion second)
		{
			first.w += second.w;
			first.x += second.x;
			first.y += second.y;
			first.z += second.z;
			return first;
		}

		private static Quaternion ScaleQuaternion(Quaternion rotation, float multiplier)
		{
			rotation.w *= multiplier;
			rotation.x *= multiplier;
			rotation.y *= multiplier;
			rotation.z *= multiplier;
			return rotation;
		}

		private static float QuaternionMagnitude(Quaternion rotation)
		{
			return Mathf.Sqrt(Quaternion.Dot(rotation, rotation));
		}

		private static Quaternion NormalizeQuaternion(Quaternion rotation)
		{
			float num = QuaternionMagnitude(rotation);
			if (num > 0f)
			{
				return ScaleQuaternion(rotation, 1f / num);
			}
			Debug.LogWarning("Cannot normalize a quaternion with zero magnitude.");
			return Quaternion.identity;
		}
	}
}
