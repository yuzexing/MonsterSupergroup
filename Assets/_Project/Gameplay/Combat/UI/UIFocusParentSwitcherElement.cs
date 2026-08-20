using DG.Tweening;
using UnityEngine;

namespace AstralShift.HellMaiden.UI
{
	public class UIFocusParentSwitcherElement : UIBaseFocusElement
	{
		[SerializeField]
		protected Transform[] focused;

		[SerializeField]
		protected Transform[] unfocused;

		private int _focusedIndex;

		private int _unfocusedIndex;

		[SerializeField]
		private Transform transformToTween;

		private Tween _moveTween;

		private Tween _scaleTween;

		public bool permanentFocus;

		public void SetFocusedParentIndex(int index, bool update = false, bool instant = false)
		{
			_focusedIndex = index;
		}

		public void SetUnFocusedParentIndex(int index)
		{
			_unfocusedIndex = index;
		}

		public void Refresh(bool instant = false)
		{
			if (_stateMachine.GetState() == Focused)
			{
				OnFocusEnter();
			}
			else if (_stateMachine.GetState() == Unfocused)
			{
				OnUnFocusEnter();
			}
		}

		public override void OnFocusEnter()
		{
			transformToTween.SetParent(focused[_focusedIndex]);
			_scaleTween?.Kill();
			_moveTween?.Kill();
			_scaleTween = transformToTween.DOScale(Vector3.one, duration).SetUpdate(isIndependentUpdate: true).SetEase(scaleEase.GetEaseFunction());
			_moveTween = transformToTween.DOLocalMove(Vector3.zero, duration).SetUpdate(isIndependentUpdate: true).SetEase(moveEase.GetEaseFunction());
		}

		public void OnFocusEnterInstant()
		{
			transformToTween.SetParent(focused[_focusedIndex]);
			_scaleTween?.Kill();
			_moveTween?.Kill();
			transformToTween.localScale = Vector3.one;
			transformToTween.localPosition = Vector3.zero;
		}

		public override void OnUnFocusEnter()
		{
			transformToTween.SetParent(unfocused[_unfocusedIndex]);
			_scaleTween?.Kill();
			_moveTween?.Kill();
			_scaleTween = transformToTween.DOScale(Vector3.one, duration).SetUpdate(isIndependentUpdate: true).SetEase(scaleEase.GetEaseFunction());
			_moveTween = transformToTween.DOLocalMove(Vector3.zero, duration).SetUpdate(isIndependentUpdate: true).SetEase(moveEase.GetEaseFunction());
		}

		public void OnUnFocusEnterInstant()
		{
			transformToTween.SetParent(unfocused[_unfocusedIndex]);
			_scaleTween?.Kill();
			_moveTween?.Kill();
			transformToTween.localScale = Vector3.one;
			transformToTween.localPosition = Vector3.zero;
		}

		private void OnDisable()
		{
			_scaleTween?.Kill();
			_moveTween?.Kill();
		}
	}
}
