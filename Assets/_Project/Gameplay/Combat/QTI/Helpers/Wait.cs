using System;
using System.Collections;
using UnityEngine;

namespace AstralShift.QTI.Helpers
{
	public static class Wait
	{
		public static IEnumerator SetTimeout(float timeout, Action action)
		{
			yield return new WaitForSeconds(timeout);
			action?.Invoke();
		}

		public static IEnumerator SetFrameTimeout(int frames, Action action)
		{
			int frameCounter = 0;
			while (frameCounter < frames)
			{
				frameCounter++;
				yield return null;
			}
			action?.Invoke();
		}

		public static IEnumerator SetUnscaledTimeout(float timeout, Action action)
		{
			yield return new WaitForSecondsRealtime(timeout);
			action?.Invoke();
		}
	}
}
