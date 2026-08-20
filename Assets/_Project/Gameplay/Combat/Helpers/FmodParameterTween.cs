using System.Collections;
using FMODUnity;
using UnityEngine;

namespace AstralShift.Helpers
{
	public class FmodParameterTween : MonoBehaviour
	{
		[SerializeField]
		private StudioEventEmitter emitter;

		[Header("Parâmetro")]
		[SerializeField]
		private string parameterName = "Phase";

		[SerializeField]
		private float fromValue;

		[SerializeField]
		private float toValue = 1f;

		[SerializeField]
		private float duration = 1f;

		private Coroutine _running;

		public void Play()
		{
			if (_running != null)
			{
				StopCoroutine(_running);
			}
			_running = StartCoroutine(TweenRoutine());
		}

		private IEnumerator TweenRoutine()
		{
			emitter.SetParameter(parameterName, fromValue, ignoreseekspeed: true);
			if (duration <= 0f)
			{
				emitter.SetParameter(parameterName, toValue, ignoreseekspeed: true);
				_running = null;
				yield break;
			}
			float elapsed = 0f;
			while (elapsed < duration)
			{
				elapsed += Time.deltaTime;
				float t = Mathf.Clamp01(elapsed / duration);
				emitter.SetParameter(parameterName, Mathf.Lerp(fromValue, toValue, t), ignoreseekspeed: true);
				yield return null;
			}
			emitter.SetParameter(parameterName, toValue, ignoreseekspeed: true);
			_running = null;
		}
	}
}
