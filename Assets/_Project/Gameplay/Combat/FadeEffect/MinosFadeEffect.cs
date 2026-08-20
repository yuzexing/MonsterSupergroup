using System;
using System.Threading;
using System.Threading.Tasks;
using Animancer;
using AstralShift.Helpers;
using UnityEngine;

namespace AstralShift.FadeEffect
{
	public class MinosFadeEffect : BaseFadeEffect
	{
		[SerializeField]
		private AnimancerComponent animancer;

		[SerializeField]
		private ClipTransition fadeInAnimation;

		[SerializeField]
		private ClipTransition fadeOutAnimation;

		public override void FadeOut(float duration, Action onEnd = null)
		{
		}

		public override async Task FadeOutTask(CancellationToken token, float duration, Action onEnd = null)
		{
			await AnimancerHelpers.AnimationTask(animancer, fadeOutAnimation);
			onEnd?.Invoke();
		}

		public override void FadeIn(float duration, Action onEnd = null)
		{
		}

		public override async Task FadeInTask(CancellationToken token, float duration, Action onEnd = null)
		{
			await AnimancerHelpers.AnimationTask(animancer, fadeInAnimation);
			onEnd?.Invoke();
		}
	}
}
