using DG.Tweening;
using TMPro;
using UnityEngine;

namespace AstralShift.HellMaiden.UI
{
	public class UIClockNumbers : MonoBehaviour
	{
		[SerializeField]
		private TextMeshProUGUI currentDigitText;

		[SerializeField]
		private TextMeshProUGUI nextDigitText;

		[SerializeField]
		private RectTransform startPosition;

		[SerializeField]
		private RectTransform endPosition;

		[SerializeField]
		private RectTransform middlePosition;

		[SerializeField]
		private CustomAnimationCurve tweenEase;

		[SerializeField]
		private float tweenDuration = 0.4f;

		private RectTransform _nextDigitRectTransform;

		private RectTransform _currentDigitRectTransform;

		private Sequence _activeSequence;

		private static readonly string[] DigitCache = new string[10] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };

		public bool Runup { get; set; } = true;

		private void Awake()
		{
			_currentDigitRectTransform = currentDigitText.GetComponent<RectTransform>();
			_nextDigitRectTransform = nextDigitText.GetComponent<RectTransform>();
			Runup = true;
		}

		public void ChangeNumber(int digit, bool animate)
		{
			if (digit < 0 || digit >= DigitCache.Length)
			{
				ChangeNumber(digit.ToString(), animate);
			}
			else
			{
				ChangeNumber(DigitCache[digit], animate);
			}
		}

		public void ChangeNumber(string number, bool animate)
		{
			if (nextDigitText.text == number)
			{
				return;
			}
			if (_activeSequence != null && _activeSequence.IsActive())
			{
				_activeSequence.Kill();
			}
			if (animate)
			{
				if (Runup)
				{
					RunUp(number);
				}
				else
				{
					RunDown(number);
				}
			}
			else
			{
				currentDigitText.text = number;
				nextDigitText.text = number;
				_nextDigitRectTransform.anchoredPosition = startPosition.anchoredPosition;
				_currentDigitRectTransform.anchoredPosition = middlePosition.anchoredPosition;
			}
		}

		private void RunUp(string number)
		{
			nextDigitText.text = number;
			_nextDigitRectTransform.position = startPosition.position;
			_currentDigitRectTransform.position = middlePosition.position;
			_activeSequence = CreateBaseSequence();
			_activeSequence.Join(_nextDigitRectTransform.DOMove(middlePosition.position, tweenDuration));
			_activeSequence.Join(_currentDigitRectTransform.DOMove(endPosition.position, tweenDuration));
			_activeSequence.OnComplete(HandleRunUpComplete);
		}

		private void RunDown(string number)
		{
			nextDigitText.text = number;
			_nextDigitRectTransform.position = endPosition.position;
			_currentDigitRectTransform.position = middlePosition.position;
			_activeSequence = CreateBaseSequence();
			_activeSequence.Join(_nextDigitRectTransform.DOMove(middlePosition.position, tweenDuration));
			_activeSequence.Join(_currentDigitRectTransform.DOMove(startPosition.position, tweenDuration));
			_activeSequence.OnComplete(HandleRunDownComplete);
		}

		private void HandleRunUpComplete()
		{
			currentDigitText.text = nextDigitText.text;
			_currentDigitRectTransform.position = middlePosition.position;
			_nextDigitRectTransform.position = startPosition.position;
		}

		private void HandleRunDownComplete()
		{
			currentDigitText.text = nextDigitText.text;
			_currentDigitRectTransform.position = middlePosition.position;
			_nextDigitRectTransform.position = endPosition.position;
		}

		private Sequence CreateBaseSequence()
		{
			return DOTween.Sequence().SetEase(tweenEase.GetEaseFunction());
		}
	}
}
