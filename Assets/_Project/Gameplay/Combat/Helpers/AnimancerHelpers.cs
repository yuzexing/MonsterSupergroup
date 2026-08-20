using System;
using System.Collections.Generic;
using System.Threading;
using Animancer;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.Helpers
{
	public class AnimancerHelpers
	{
		public class WaitForAnimationEnd : CustomYieldInstruction
		{
			private bool _isComplete;

			public override bool keepWaiting => !_isComplete;

			public WaitForAnimationEnd(object owner, List<AnimancerState> states)
			{
				WaitForAnimationEnd waitForAnimationEnd = this;
				if (states == null || states.Count == 0)
				{
					_isComplete = true;
					return;
				}
				_isComplete = false;
				int remainingAnimations = states.Count;
				foreach (AnimancerState state in states)
				{
					ref Action onEnd = ref state.Events(owner).OnEnd;
					onEnd = (Action)Delegate.Combine(onEnd, (Action)delegate
					{
						remainingAnimations--;
						if (remainingAnimations <= 0)
						{
							waitForAnimationEnd._isComplete = true;
						}
					});
				}
			}

			public WaitForAnimationEnd(object owner, AnimancerState state)
			{
				if (state == null)
				{
					_isComplete = true;
					return;
				}
				_isComplete = false;
				ref Action onEnd = ref state.Events(owner).OnEnd;
				onEnd = (Action)Delegate.Combine(onEnd, (Action)delegate
				{
					_isComplete = true;
				});
			}
		}

		public static async UniTask AnimationTask(AnimancerComponent animancer, ClipTransition clipTransition, int layer = 0, CancellationToken externalToken = default(CancellationToken), FadeMode fadeMode = FadeMode.FixedSpeed)
		{
			await UniTask.WaitForEndOfFrame(externalToken);
			if (externalToken.IsCancellationRequested)
			{
				return;
			}
			// AnimancerState animationState = animancer.Layers[layer].Play(clipTransition, clipTransition.FadeDuration, fadeMode);
			// while (animationState != null && animationState.IsPlayingAndNotEnding())
			// {
			// 	await UniTask.NextFrame(externalToken);
			// 	if (!animancer)
			// 	{
			// 		break;
			// 	}
			// }
		}

		public static async UniTask AnimationTask(AnimancerState state, CancellationToken externalToken = default(CancellationToken))
		{
			await UniTask.WaitForEndOfFrame(externalToken);
			if (!externalToken.IsCancellationRequested)
			{
				while (state != null && state.IsPlayingAndNotEnding())
				{
					await UniTask.NextFrame(externalToken);
				}
			}
		}
	}
}
