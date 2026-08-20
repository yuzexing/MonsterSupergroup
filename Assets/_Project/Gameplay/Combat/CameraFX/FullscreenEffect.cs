using UnityEngine;

namespace AstralShift.HellMaiden.CameraFX
{
	[RequireComponent(typeof(CanvasGroup))]
	public abstract class FullscreenEffect : MonoBehaviour
	{
		public CanvasGroup canvasGroup;

		private void Reset()
		{
			canvasGroup = GetComponent<CanvasGroup>();
		}

		public abstract void Trigger();

		public abstract void Enable();

		public abstract void Disable();
	}
}
