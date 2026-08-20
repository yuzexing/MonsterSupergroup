using AstralShift.HellMaiden.Characters;
using AstralShift.HellMaiden.Common;
using AstralShift.HellMaiden.Timeline.TransformTween;
using UnityEngine;
using UnityEngine.Playables;

namespace AstralShift.HellMaiden.Timeline
{
	public class CharacterMovementTrackMixer : TransformTweenMixerBehaviour
	{
		private Vector2 lastPosition = Vector2.positiveInfinity;

		private Vector2 dirNormalized;

		private bool isMoonwalking;

		private bool inInput;

		public override void ProcessFrame(Playable playable, FrameData info, object playerData)
		{
			inInput = false;
			CharacterMovement component = (playerData as Transform).GetComponent<CharacterMovement>();
			if (component == null)
			{
				Debug.LogError("<color=green><b>Character movement track without binding!</b></color>");
				return;
			}
			bool flag = true;
			int inputCount = playable.GetInputCount();
			for (int i = 0; i < inputCount; i++)
			{
				if (playable.GetInputWeight(i) > 0f)
				{
					TransformTweenBehaviour transformTweenBehaviour = null;
					ScriptPlayable<TransformTweenBehaviour> scriptPlayable = default(ScriptPlayable<TransformTweenBehaviour>);
					try
					{
						scriptPlayable = (ScriptPlayable<TransformTweenBehaviour>)playable.GetInput(i);
						transformTweenBehaviour = scriptPlayable.GetBehaviour();
						_ = (Vector2)transformTweenBehaviour.endLocation.position;
						inInput = true;
					}
					catch
					{
						continue;
					}
					isMoonwalking = transformTweenBehaviour.isMoonwalking;
					if (typeof(AnimationAndTweenBehaviour) == scriptPlayable.GetType())
					{
						flag = false;
					}
				}
			}
			base.ProcessFrame(playable, info, playerData);
			if (inInput && lastPosition != Vector2.positiveInfinity)
			{
				Vector2 vector = blendedPosition - lastPosition;
				dirNormalized = vector.normalized;
				_ = vector / Time.unscaledDeltaTime;
				if (flag && dirNormalized.ToDirection() != Direction.None)
				{
					if (isMoonwalking)
					{
						dirNormalized = -dirNormalized;
					}
					component.SetDirectionImmediate(dirNormalized);
				}
			}
			lastPosition = blendedPosition;
		}
	}
}
