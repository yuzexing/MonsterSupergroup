using Animancer;
using AstralShift.HellMaiden.Characters;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Timeline.TransformTween;
using UnityEngine;
using UnityEngine.Playables;

namespace AstralShift.HellMaiden.Timeline
{
	public class CharacterAnimationBehaviour : TransformTweenBehaviour
	{
		public AnimationClip animation;

		public int layer;

		public bool blockOtherAnimations;

		private bool _isFirstFrame = true;

		private CharacterMovement _character;

		private CharacterAnimator _characterAnimator;

		public override void ProcessFrame(Playable playable, FrameData info, object playerData)
		{
			base.ProcessFrame(playable, info, playerData);
			_character = (playerData as Transform).GetComponent<CharacterMovement>();
			if ((bool)_character && _isFirstFrame)
			{
				_characterAnimator = _character.animator;
				ClipTransition clipTransition = new ClipTransition();
				clipTransition.Clip = animation;
				_characterAnimator.PlayOverridenAnimations(clipTransition, layer, resetOnEnd: false, blockOtherAnimations);
				_isFirstFrame = false;
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
					StopAnimation();
					OnEnd();
				}
			}
		}

		protected void StopAnimation()
		{
			_characterAnimator.ResetAnimancer();
			_isFirstFrame = true;
		}
	}
}
