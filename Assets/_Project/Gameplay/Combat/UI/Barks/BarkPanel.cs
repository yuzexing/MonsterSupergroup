using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI.Barks
{
	public class BarkPanel : MonoBehaviour
	{
		[SerializeField]
		private TextMeshProUGUI stringText;

		[SerializeField]
		private CanvasGroup canvasGroup;

		[SerializeField]
		private float fadeDuration = 0.2f;

		private RectTransform _rectTransform;

		[Header("Positions")]
		[SerializeField]
		private Image middleDownLeftImage;

		[SerializeField]
		private Image middleDownRightImage;

		[SerializeField]
		private Image downRightImage;

		[SerializeField]
		private Image downLeftImage;

		[SerializeField]
		private Image middleUpLeftImage;

		[SerializeField]
		private Image middleUpRightImage;

		[SerializeField]
		private Image upRightImage;

		[SerializeField]
		private Image upLeftImage;

		private BarkBalloonDirections _currentBarkBalloonDirection = BarkBalloonDirections.None;

		private Image _activeImage;

		private Tween _showHideTween;

		public RectTransform RectTransform
		{
			get
			{
				if (!_rectTransform)
				{
					TryGetComponent<RectTransform>(out _rectTransform);
				}
				return _rectTransform;
			}
		}

		private void Start()
		{
			Hide();
			middleDownLeftImage.gameObject.SetActive(value: false);
			middleDownRightImage.gameObject.SetActive(value: false);
			downRightImage.gameObject.SetActive(value: false);
			downLeftImage.gameObject.SetActive(value: false);
			middleUpLeftImage.gameObject.SetActive(value: false);
			middleUpRightImage.gameObject.SetActive(value: false);
			upRightImage.gameObject.SetActive(value: false);
			upLeftImage.gameObject.SetActive(value: false);
		}

		public void SetText(string text)
		{
			stringText.text = text;
		}

		public void Show()
		{
			_showHideTween?.Kill();
			canvasGroup.DOFade(1f, fadeDuration);
		}

		public void Hide()
		{
			_showHideTween?.Kill();
			canvasGroup.DOFade(0f, fadeDuration);
		}

		public void SetDirection(BarkBalloonDirections newBarkBalloonDirection)
		{
			if (newBarkBalloonDirection != _currentBarkBalloonDirection)
			{
				_activeImage?.gameObject.SetActive(value: false);
				if (!VerifyAndChangeActiveDirection(BarkBalloonDirections.Down, newBarkBalloonDirection, middleDownRightImage) && !VerifyAndChangeActiveDirection(BarkBalloonDirections.MiddleDownRight, newBarkBalloonDirection, middleDownRightImage) && !VerifyAndChangeActiveDirection(BarkBalloonDirections.MiddleDownLeft, newBarkBalloonDirection, middleDownLeftImage) && !VerifyAndChangeActiveDirection(BarkBalloonDirections.DownRight, newBarkBalloonDirection, downRightImage) && !VerifyAndChangeActiveDirection(BarkBalloonDirections.DownLeft, newBarkBalloonDirection, downLeftImage) && !VerifyAndChangeActiveDirection(BarkBalloonDirections.Up, newBarkBalloonDirection, middleUpRightImage) && !VerifyAndChangeActiveDirection(BarkBalloonDirections.MiddleUpRight, newBarkBalloonDirection, middleUpRightImage) && !VerifyAndChangeActiveDirection(BarkBalloonDirections.MiddleUpLeft, newBarkBalloonDirection, middleUpLeftImage) && !VerifyAndChangeActiveDirection(BarkBalloonDirections.UpLeft, newBarkBalloonDirection, upLeftImage))
				{
					VerifyAndChangeActiveDirection(BarkBalloonDirections.UpRight, newBarkBalloonDirection, upRightImage);
				}
			}
		}

		private bool VerifyAndChangeActiveDirection(BarkBalloonDirections barkBalloonDirectionToCheck, BarkBalloonDirections newBarkBalloonDirection, Image imageToChangeTo)
		{
			if (newBarkBalloonDirection != barkBalloonDirectionToCheck)
			{
				return false;
			}
			_activeImage = imageToChangeTo;
			_currentBarkBalloonDirection = newBarkBalloonDirection;
			_activeImage.gameObject.SetActive(value: true);
			return true;
		}
	}
}
