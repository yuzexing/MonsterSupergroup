using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI.HUD
{
	public class ShrinesHolderIcon : MonoBehaviour
	{
		[SerializeField]
		private List<Image> icons;

		[SerializeField]
		private RectTransform plusIcon;

		[SerializeField]
		private Vector2 spacing;

		private RectTransform _rectTransform;

		public void Init(Sprite iconSprite)
		{
			_rectTransform = GetComponent<RectTransform>();
			UpdateSprites(iconSprite);
			UpdatePositions();
			UpdateCount(1);
		}

		private void UpdateSprites(Sprite iconSprite)
		{
			for (int i = 0; i < icons.Count; i++)
			{
				Image image = icons[i];
				image.sprite = iconSprite;
				image.rectTransform.sizeDelta = new Vector2(iconSprite.rect.width / 2f, iconSprite.rect.height / 2f);
			}
		}

		private void UpdatePositions()
		{
			for (int i = 0; i < icons.Count; i++)
			{
				icons[i].rectTransform.anchoredPosition = new Vector2((float)i * spacing.x, (float)i * spacing.y);
			}
		}

		public void UpdateCount(int count)
		{
			int num = Mathf.Min(count, icons.Count);
			for (int i = 0; i < icons.Count; i++)
			{
				icons[i].gameObject.SetActive(i < num);
				float num2 = (float)(i + 1) / (float)num;
				icons[i].color = new Color(num2, num2, num2, 1f);
			}
			plusIcon.gameObject.SetActive(count > icons.Count);
			Vector2 sizeDelta = icons[0].rectTransform.sizeDelta;
			float num3 = spacing.x * (float)(num - 1);
			_rectTransform.sizeDelta = new Vector2(sizeDelta.x + num3, sizeDelta.y);
		}
	}
}
