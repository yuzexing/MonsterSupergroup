using System;
using Animancer;
using UnityEngine;

namespace AstralShift.HellMaiden.Quests
{
	public class FollowQuestAnimator : MonoBehaviour
	{
		public AnimancerComponent animancer;

		public ClipTransition enter;

		public ClipTransition moving;

		public ClipTransition waiting;

		public ClipTransition exit;

		public void PlayEnterAnimation()
		{
			ref Action onEnd = ref enter.Events.OnEnd;
			onEnd = (Action)Delegate.Combine(onEnd, new Action(PlayWaitingAnimation));
			animancer.Play(enter);
		}

		public void PlayMovingAnimation()
		{
			animancer.Play(moving);
		}

		public void PlayWaitingAnimation()
		{
			animancer.Play(waiting);
		}

		public void PlayExitAnimation()
		{
			animancer.Play(exit);
		}
	}
}
