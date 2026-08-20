using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI.HUD
{
	public class ShrinesHolderTemporaryIcon : MonoBehaviour
	{
		[SerializeField]
		private Image iconBG;

		[SerializeField]
		private Image iconFill;

		private RectTransform _rectTransform;

		public void Init(Sprite iconSprite, Func<float> remainingTime, float duration)
		{
			_rectTransform = GetComponent<RectTransform>();
			SetSiblingIndex();
			UpdateSprites(iconSprite);
			RunFillAnimation(remainingTime, duration).Forget();
		}

		private void UpdateSprites(Sprite iconSprite)
		{
			iconBG.sprite = iconSprite;
			iconBG.rectTransform.sizeDelta = new Vector2(iconSprite.rect.width / 2f, iconSprite.rect.height / 2f);
			iconFill.sprite = iconSprite;
			iconFill.rectTransform.sizeDelta = new Vector2(iconSprite.rect.width / 2f, iconSprite.rect.height / 2f);
			iconFill.fillAmount = 1f;
		}

		private async UniTaskVoid RunFillAnimation(Func<float> remainingTime, float duration)
		{
			try
			{
				for (float num = remainingTime(); num > 0f; num = remainingTime())
				{
					float fillAmount = num / duration;
					iconFill.fillAmount = fillAmount;
					await UniTask.NextFrame(base.destroyCancellationToken);
				}
				iconFill.fillAmount = 0f;
			}
			catch (OperationCanceledException)
			{
			}
		}

		private void SetSiblingIndex()
		{
			_rectTransform.SetSiblingIndex(_rectTransform.parent.childCount - 1);
		}
	}
}
