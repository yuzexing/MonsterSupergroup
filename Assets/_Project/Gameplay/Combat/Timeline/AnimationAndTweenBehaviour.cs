using UnityEngine;
using UnityEngine.Playables;

namespace AstralShift.HellMaiden.Timeline
{
	public class AnimationAndTweenBehaviour : CharacterAnimationBehaviour
	{
		public override void OnBehaviourPlay(Playable playable, FrameData info)
		{
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
					StopAnimation();
					OnEnd();
				}
			}
		}
	}
}
