using UnityEngine;
using UnityEngine.Events;

namespace AstralShift.HellMaiden.CameraFX
{
	public class ResolutionChangeInvoker : MonoBehaviour
	{
		[SerializeField]
		protected UnityEvent onResolutionChange;

		private void Start()
		{
			GameDirector.Instance.Settings.OnResolutionChanged += Invoke;
		}

		private void OnDestroy()
		{
			GameDirector.Instance.Settings.OnResolutionChanged -= Invoke;
		}

		private void Invoke()
		{
			onResolutionChange?.Invoke();
		}
	}
}
