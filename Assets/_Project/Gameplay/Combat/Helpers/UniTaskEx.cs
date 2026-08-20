using System;
using Cysharp.Threading.Tasks;
using UnityEngine.Playables;

namespace AstralShift.Helpers
{
	public static class UniTaskEx
	{
		public static async void OnEndCallback(this PlayableDirector director, PlayerLoopTiming timing = PlayerLoopTiming.PostLateUpdate, Action callback = null)
		{
			try
			{
				await UniTask.WaitUntil(() => director.time >= director.duration, timing);
				callback?.Invoke();
			}
			catch (Exception)
			{
			}
		}

		public static async void OnEndCallback(this PlayableDirector director, Action callback = null)
		{
			director.OnEndCallback(PlayerLoopTiming.PostLateUpdate, callback);
		}
	}
}
