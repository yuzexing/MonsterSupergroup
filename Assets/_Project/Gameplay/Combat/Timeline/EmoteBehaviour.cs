using AstralShift.HellMaiden.Characters;
using AstralShift.HellMaiden.Characters.Effects;
using UnityEngine;
using UnityEngine.Playables;

namespace AstralShift.HellMaiden.Timeline
{
	public class EmoteBehaviour : PlayableBehaviour
	{
		public CharacterBalloonController.EmojiType emoji;

		private bool firstFrameHappenedEmote;

		public override void ProcessFrame(Playable playable, FrameData info, object playerData)
		{
			if (!firstFrameHappenedEmote)
			{
				firstFrameHappenedEmote = true;
				CharacterMovement component = (playerData as Transform).GetComponent<CharacterMovement>();
				if (emoji != CharacterBalloonController.EmojiType.None)
				{
					component.GetComponentInChildren<CharacterBalloonController>().DisplayEmoji(emoji);
				}
			}
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
					OnEnd();
				}
			}
		}

		protected void OnEnd()
		{
			firstFrameHappenedEmote = false;
		}
	}
}
