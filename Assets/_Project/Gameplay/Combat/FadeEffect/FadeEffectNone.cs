using System;
using System.Threading;
using System.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.FadeEffect
{
	public class FadeEffectNone : BaseFadeEffect
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
			_material.color = new Color(0f, 0f, 0f, 1f);
			onEnd?.Invoke();
		}

		public override async Task FadeOutTask(CancellationToken token, float duration, Action onEnd = null)
		{
			FadeOut(duration, onEnd);
			await Task.CompletedTask;
		}

		public override void FadeIn(float duration, Action onEnd = null)
		{
			if (_material == null)
			{
				_material = new Material(panel.materialForRendering);
				panel.material = _material;
			}
			_material.color = new Color(0f, 0f, 0f, 0f);
			onEnd?.Invoke();
		}

		public override async Task FadeInTask(CancellationToken token, float duration, Action onEnd = null)
		{
			FadeIn(duration, onEnd);
			await Task.CompletedTask;
		}

		private void OnDestroy()
		{
			_tween?.Kill();
		}
	}
}
