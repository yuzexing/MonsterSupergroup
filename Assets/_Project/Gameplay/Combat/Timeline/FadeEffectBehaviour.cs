using AstralShift.FadeEffect;
using UnityEngine;
using UnityEngine.Playables;

namespace AstralShift.HellMaiden.Timeline
{
	public class FadeEffectBehaviour : PlayableBehaviour
	{
		public bool firstTimePlaying = true;

		public Color color = Color.black;

		public FadeEffectClip.FadeClipType fadeType;

		public FadeEffectEnum fadeEffect;

		private ScreenFader _screenFader;

		public override void ProcessFrame(Playable playable, FrameData info, object playerData)
		{
			if (!firstTimePlaying)
			{
				return;
			}
			_screenFader = ScreenFader.Instance;
			if (fadeType == FadeEffectClip.FadeClipType.FadeIn)
			{
				if (_screenFader.stateMachine.GetState() != _screenFader.FadedOut)
				{
					_screenFader.FadeOut(FadeEffectEnum.None, 0f);
				}
				_screenFader.FadeIn(fadeEffect, (float)playable.GetDuration());
			}
			if (fadeType == FadeEffectClip.FadeClipType.FadeOut)
			{
				if (_screenFader.stateMachine.GetState() != _screenFader.FadedIn)
				{
					_screenFader.FadeIn(FadeEffectEnum.None, 0f);
				}
				_screenFader.FadeOut(fadeEffect, (float)playable.GetDuration());
			}
			playable.GetGraph().GetResolver();
			firstTimePlaying = false;
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
					Debug.Log("Clip done!");
				}
			}
		}
	}
}
