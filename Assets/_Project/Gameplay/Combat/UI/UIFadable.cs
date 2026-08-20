using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AstralShift.UI
{
	[RequireComponent(typeof(CanvasGroup))]
	public class UIFadable : UIBehaviour
	{
		[SerializeField]
		private float fadeDuration = 0.2f;

		private Tween _fadeTween;

		protected CanvasGroup _canvasGroup;

		public bool blocksRaycasts
		{
			get
			{
				if (!_canvasGroup)
				{
					TryGetComponent<CanvasGroup>(out _canvasGroup);
				}
				return _canvasGroup.blocksRaycasts;
			}
			set
			{
				if (!_canvasGroup)
				{
					TryGetComponent<CanvasGroup>(out _canvasGroup);
				}
				_canvasGroup.blocksRaycasts = value;
			}
		}

		public virtual void Show()
		{
			if (_canvasGroup == null)
			{
				TryGetComponent<CanvasGroup>(out _canvasGroup);
			}
			blocksRaycasts = true;
			_fadeTween.Kill();
			_fadeTween = _canvasGroup.DOFade(1f, fadeDuration).SetUpdate(isIndependentUpdate: true).OnComplete(delegate
			{
				_canvasGroup.interactable = true;
			});
			_fadeTween.Play();
		}

		public virtual void Hide()
		{
			if (_canvasGroup == null)
			{
				TryGetComponent<CanvasGroup>(out _canvasGroup);
			}
			blocksRaycasts = false;
			_canvasGroup.interactable = false;
			_fadeTween.Kill();
			_canvasGroup.DOFade(0f, fadeDuration).SetUpdate(isIndependentUpdate: true);
			_fadeTween.Play();
		}
	}
}
