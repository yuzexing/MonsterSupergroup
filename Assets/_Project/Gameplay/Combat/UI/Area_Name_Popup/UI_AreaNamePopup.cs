using System;
using System.Threading;
using Animancer;
using AstralShift.HellMaiden.Combat;
using AstralShift.Helpers;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Serialization;

namespace AstralShift.HellMaiden.UI.Area_Name_Popup
{
	public class UI_AreaNamePopup : MonoBehaviour
	{
		[SerializeField]
		private AnimancerComponent animancerComponent;

		[FormerlySerializedAs("songPopupAppear")]
		[SerializeField]
		private ClipTransition areaPopupAppear;

		[FormerlySerializedAs("songPopupDisappear")]
		[SerializeField]
		private ClipTransition areaPopupDisappear;

		private float waitDuration = 1f;

		private CancellationTokenSource _cts;

		public async void ShowPopup()
		{
			_ = 2;
			try
			{
				_cts = new CancellationTokenSource();
				await AnimancerHelpers.AnimationTask(animancerComponent, areaPopupAppear, 0, _cts.Token);
				await UniTask.WaitForSeconds(waitDuration, ignoreTimeScale: false, PlayerLoopTiming.Update, _cts.Token, cancelImmediately: true);
				await AnimancerHelpers.AnimationTask(animancerComponent, areaPopupDisappear, 0, _cts.Token);
				GameEvents.Instance.OnAreaNamePopupClosed?.Invoke();
				GameEvents.Instance.OnAreaNamePopupClosed = null;
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception message)
			{
				Debug.LogError(message);
			}
			finally
			{
				_cts?.Dispose();
				_cts = null;
			}
		}

		private void OnDestroy()
		{
			GameEvents.Instance.OnAreaNamePopupClosed = null;
			_cts?.Cancel();
			_cts?.Dispose();
			_cts = null;
		}
	}
}
