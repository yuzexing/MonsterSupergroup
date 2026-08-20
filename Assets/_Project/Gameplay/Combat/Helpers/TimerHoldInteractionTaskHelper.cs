using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace AstralShift.Helpers
{
	public static class TimerHoldInteractionTaskHelper
	{
		private static CancellationTokenSource _holdCts;

		private static bool _isActive;

		public static async UniTask<bool> ProcessHoldAsync(float holdDuration, Action onHeldCompleted, CancellationToken externalToken = default(CancellationToken))
		{
			if (_isActive)
			{
				return false;
			}
			_isActive = true;
			_holdCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
			try
			{
				await UniTask.WaitForSeconds(holdDuration, ignoreTimeScale: true, PlayerLoopTiming.Update, _holdCts.Token);
				onHeldCompleted?.Invoke();
				return true;
			}
			catch (OperationCanceledException)
			{
				return false;
			}
			finally
			{
				_isActive = false;
				_holdCts?.Dispose();
				_holdCts = null;
			}
		}

		public static void CancelAndDispose()
		{
			_holdCts?.Cancel();
			_holdCts?.Dispose();
		}
	}
}
