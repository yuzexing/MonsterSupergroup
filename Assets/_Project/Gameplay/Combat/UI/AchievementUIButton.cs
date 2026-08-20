using AstralShift.HellMaiden.UI.Menus.Achievement;
using AstralShift.UI;
using UnityEngine;
using UnityEngine.UI;

public class AchievementUIButton : CustomUIButton
{
	[SerializeField]
	private Image iconImage;

	[SerializeField]
	private Image chainImage;

	[SerializeField]
	private Image backgroundImage;

	[SerializeField]
	private AchievementData achievementData;

	public AchievementData GetAchievementData()
	{
		return achievementData;
	}

	public void Initialize(AchievementData ad)
	{
		achievementData = ad;
	}

	public void SetSprite(Sprite sprite)
	{
		if (iconImage != null)
		{
			iconImage.sprite = sprite;
		}
	}

	public void SetUnlockedBackgroundSprite(Sprite sprite)
	{
		chainImage.enabled = false;
		if (backgroundImage != null)
		{
			backgroundImage.sprite = sprite;
		}
	}

	public string GetLocalizedTitle()
	{
		if (!(achievementData != null))
		{
			return "";
		}
		return achievementData.LocalizedTitle;
	}

	public string GetLocalizedDescription()
	{
		if (!(achievementData != null))
		{
			return "";
		}
		return achievementData.LocalizedDescription;
	}
}
