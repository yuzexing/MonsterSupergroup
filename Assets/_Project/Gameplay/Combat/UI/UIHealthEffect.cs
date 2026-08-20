using System.Collections;
using AstralShift.HellMaiden.CameraFX;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI
{
	public class UIHealthEffect : FullscreenEffect
	{
		public Image fill;

		public Image[] corners;

		private Coroutine _triggerCoroutine;

		private Tween _constantTween;

		private float _currentAlpha;

		protected void Awake()
		{
			fill.color = Color.clear;
			Image[] array = corners;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].color = Color.clear;
			}
		}

		public override void Trigger()
		{
			if (_triggerCoroutine != null)
			{
				StopCoroutine(_triggerCoroutine);
			}
			_triggerCoroutine = StartCoroutine(TriggerAnimation());
		}

		private IEnumerator TriggerAnimation()
		{
			fill.color = Color.white;
			float currentAlpha = 1f;
			Tween t = DOTween.To(() => currentAlpha, delegate(float result)
			{
				currentAlpha = result;
				fill.color = new Color(1f, 1f, 1f, currentAlpha);
			}, 0f, 0.75f).SetDelay(0.1f);
			yield return t.WaitForCompletion();
			_triggerCoroutine = null;
		}

		public override void Enable()
		{
			Image[] array = corners;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].color = Color.white;
			}
			_constantTween?.Kill();
			_currentAlpha = 1f;
			_constantTween = DOTween.To(() => _currentAlpha, delegate(float result)
			{
				_currentAlpha = result;
				Image[] array2 = corners;
				for (int j = 0; j < array2.Length; j++)
				{
					array2[j].color = new Color(1f, 1f, 1f, _currentAlpha);
				}
			}, 0f, 1f).SetDelay(1f).SetLoops(-1, LoopType.Yoyo);
		}

		public override void Disable()
		{
			_constantTween?.Kill();
			_constantTween = DOTween.To(() => _currentAlpha, delegate(float result)
			{
				_currentAlpha = result;
				Image[] array = corners;
				for (int i = 0; i < array.Length; i++)
				{
					array[i].color = new Color(1f, 1f, 1f, _currentAlpha);
				}
			}, 0f, 0.5f);
		}
	}
}
