using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AstralShift.FadeEffect
{
	public abstract class BaseFadeEffect : MonoBehaviour
	{
		public abstract void FadeOut(float duration, Action onEnd = null);

		public abstract Task FadeOutTask(CancellationToken token, float duration, Action onEnd = null);

		public abstract void FadeIn(float duration, Action onEnd = null);

		public abstract Task FadeInTask(CancellationToken token, float duration, Action onEnd = null);
	}
}
