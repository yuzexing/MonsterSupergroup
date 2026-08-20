using AstralShift.HellMaiden.Characters;
using AstralShift.HellMaiden.Common;
using AstralShift.HellMaiden.Timeline.TransformTween;
using UnityEngine;
using UnityEngine.Playables;

namespace AstralShift.HellMaiden.Timeline
{
	public class SetDirectionBehaviour : TransformTweenBehaviour
	{
		public Direction directionToFace;

		private bool firstFrameHappened;

		public double duration;

		public int framesPerSprite;

		private CharacterMovement character;

		public override void ProcessFrame(Playable playable, FrameData info, object playerData)
		{
			playable.GetGraph().GetResolver();
			character = (playerData as Transform).GetComponent<CharacterMovement>();
			base.ProcessFrame(playable, info, playerData);
			if (!firstFrameHappened)
			{
				firstFrameHappened = true;
				character.SetDirectionImmediate(directionToFace.ToVector2());
				character.StopMovement();
			}
		}

		public override void OnBehaviourPause(Playable playable, FrameData info)
		{
			if (!Application.isPlaying)
			{
				return;
			}
			double num = playable.GetDuration();
			double time = playable.GetTime();
			double num2 = time + (double)info.deltaTime;
			if ((info.effectivePlayState == PlayState.Paused && num2 > num) || Mathf.Approximately((float)time, (float)num))
			{
				Debug.Log("Set Direction Clip done for: " + character.name);
				if (!firstFrameHappened)
				{
					character.SetDirectionImmediate(directionToFace.ToVector2());
					character.StopMovement();
				}
				firstFrameHappened = false;
				OnEnd();
			}
		}
	}
}
