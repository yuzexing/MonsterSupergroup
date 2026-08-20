using System;
using System.Threading;
using System.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.FadeEffect
{
	public class DefaultFadeEffect : BaseFadeEffect
	{
		public Image panel;

		private Tween _tween;

		private Material _material;

		private void Awake()
		{
			if (_material == null)
			{
				_material = new Material(panel.materialForRendering);
				panel.material = _material;
			}
		}

		public override void FadeOut(float duration, Action onEnd = null)
		{
			if (_material == null)
			{
				_material = new Material(panel.materialForRendering);
				panel.material = _material;
			}
			_material.color = new Color(0f, 0f, 0f, 0f);
			_tween?.Kill();
			_tween = _material.DOFade(1f, duration);
			_tween.SetUpdate(isIndependentUpdate: true);
			_tween.onComplete = delegate
			{
				onEnd?.Invoke();
			};
			_tween.Restart();
		}

		public override async Task FadeOutTask(CancellationToken token, float duration, Action onEnd = null)
		{
			if (_material == null)
			{
				_material = new Material(panel.materialForRendering);
				panel.material = _material;
			}
			_material.color = new Color(0f, 0f, 0f, 0f);
			_tween?.Kill();
			_tween = _material.DOFade(1f, duration);
			_tween.SetUpdate(isIndependentUpdate: true);
			_tween.onUpdate = delegate
			{
				if (token.IsCancellationRequested)
				{
					_tween.Kill();
				}
			};
			_tween.onComplete = delegate
			{
				onEnd?.Invoke();
			};
			_tween.Restart();
			await _tween.AsyncWaitForCompletion();
		}

		public override void FadeIn(float duration, Action onEnd = null)
		{
			if (_material == null)
			{
				_material = new Material(panel.materialForRendering);
				panel.material = _material;
			}
			_material.color = new Color(0f, 0f, 0f, 1f);
			_tween?.Kill();
			_tween = _material.DOFade(0f, duration);
			_tween.SetUpdate(isIndependentUpdate: true);
			_tween.onComplete = delegate
			{
				onEnd?.Invoke();
			};
			_tween.Restart();
		}

		public override async Task FadeInTask(CancellationToken token, float duration, Action onEnd = null)
		{
			if (_material == null)
			{
				_material = new Material(panel.materialForRendering);
				panel.material = _material;
			}
			_material.color = new Color(0f, 0f, 0f, 1f);
			_tween?.Kill();
			_tween = _material.DOFade(0f, duration);
			_tween.SetUpdate(isIndependentUpdate: true);
			_tween.onUpdate = delegate
			{
				if (token.IsCancellationRequested)
				{
					_tween.Kill();
				}
			};
			_tween.onComplete = delegate
			{
				onEnd?.Invoke();
			};
			_tween.Restart();
			await _tween.AsyncWaitForCompletion();
		}

		private void OnDestroy()
		{
			_tween?.Kill();
		}
	}
}
