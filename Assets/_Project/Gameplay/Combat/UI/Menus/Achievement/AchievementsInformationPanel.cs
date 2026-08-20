using AstralShift.HellMaiden.Data;
using AstralShift.Helpers.Attributes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI.Menus.Achievement
{
	public class AchievementsInformationPanel : MonoBehaviour
	{
		public Image iconImage;

		public TMP_Text titleText;

		public TMP_Text descriptionText;

		[Header("Display Panel")]
		[SerializeField]
		private GameObject commonDisplay;

		[SerializeField]
		private GameObject rareDisplay;

		[Header("Progress Bar")]
		[SerializeField]
		private AchievementInfoPanelProgressBar progressBar;

		[SerializeField]
		private GameObject progressBarContainer;

		[Header("Achievement Prefabs")]
		[SerializeField]
		[NotNullRef]
		private GameObject normalAchievementInfoPanel;

		[SerializeField]
		[NotNullRef]
		private GameObject rareAchievementInfoPanel;

		[SerializeField]
		[NotNullRef]
		private GameObject secretAchievementInfoPanel;

		private AchievementData currentAchievement;

		public void DisplayAchievement(AchievementData achievement)
		{
			if (!(achievement == null))
			{
				currentAchievement = achievement;
				DeactivateAllPanels();
				if (achievement.IsSecret && !AchievementManager.Instance.IsAchievementUnlockedInGameData(achievement))
				{
					secretAchievementInfoPanel.SetActive(value: true);
					DisplaySecretAchievement(achievement);
				}
				else if (achievement.Rarity == RarityType.Rare)
				{
					rareAchievementInfoPanel.SetActive(value: true);
					DisplayRareAchievement(achievement);
				}
				else
				{
					normalAchievementInfoPanel.SetActive(value: true);
					DisplayNormalAchievement(achievement);
				}
			}
		}

		private void DeactivateAllPanels()
		{
			if (normalAchievementInfoPanel != null)
			{
				normalAchievementInfoPanel.SetActive(value: false);
			}
			if (rareAchievementInfoPanel != null)
			{
				rareAchievementInfoPanel.SetActive(value: false);
			}
			if (secretAchievementInfoPanel != null)
			{
				secretAchievementInfoPanel.SetActive(value: false);
			}
		}

		private void DisplaySecretAchievement(AchievementData achievement)
		{
			ResetDisplays();
			commonDisplay.SetActive(value: true);
			iconImage = commonDisplay.GetComponentInChildren<Image>();
			if (iconImage != null)
			{
				iconImage.enabled = false;
			}
			titleText.text = "???";
			descriptionText.text = achievement.LocalizedDescription;
			HideProgressBar();
		}

		private void DisplayNormalAchievement(AchievementData achievement)
		{
			ResetDisplays();
			commonDisplay.SetActive(value: true);
			iconImage = commonDisplay.transform.GetChild(0).GetComponent<Image>();
			RenderChainsCondition(achievement.LinkedAchievementID, commonDisplay);
			SetupTextAndIcon(achievement);
			UpdateProgressDisplay(achievement);
		}

		private void DisplayRareAchievement(AchievementData achievement)
		{
			ResetDisplays();
			rareDisplay.SetActive(value: true);
			iconImage = rareDisplay.transform.GetChild(0).GetComponent<Image>();
			SetupTextAndIcon(achievement);
			if (iconImage != null && iconImage.sprite != null)
			{
				iconImage.SetNativeSize();
			}
			RenderChainsCondition(achievement.LinkedAchievementID, rareDisplay);
			UpdateProgressDisplay(achievement);
		}

		private void RenderChainsCondition(AchievementManager.AchievementID id, GameObject display)
		{
			if (AchievementManager.Instance.IsAchievementUnlockedInGameData(id))
			{
				display.transform.GetChild(1).GetComponent<Image>().enabled = false;
			}
			else
			{
				display.transform.GetChild(1).GetComponent<Image>().enabled = true;
			}
		}

		private void ResetDisplays()
		{
			if (commonDisplay != null)
			{
				commonDisplay.SetActive(value: false);
			}
			if (rareDisplay != null)
			{
				rareDisplay.SetActive(value: false);
			}
			if (iconImage != null)
			{
				iconImage.enabled = false;
			}
		}

		private void SetupTextAndIcon(AchievementData achievement)
		{
			titleText.text = achievement.LocalizedTitle;
			descriptionText.text = achievement.LocalizedDescription;
			if (iconImage != null && achievement.Icon != null)
			{
				iconImage.enabled = true;
				iconImage.sprite = achievement.Icon;
			}
		}

		public void RefreshProgress()
		{
			if (currentAchievement != null && !currentAchievement.IsSecret)
			{
				UpdateProgressDisplay(currentAchievement);
			}
		}

		private void UpdateProgressDisplay(AchievementData achievement, bool animate = false)
		{
			if (!achievement.HasProgressToTrack || progressBar == null)
			{
				HideProgressBar();
				return;
			}
			int currentProgress = GetCurrentProgress(achievement);
			int num = Mathf.RoundToInt(achievement.TargetProgress);
			ShowProgressBar();
			if (animate)
			{
				progressBar.SetTotal(achievement.TargetProgress);
				progressBar.SetProgress(currentProgress);
			}
			else
			{
				progressBar.Initialize(currentProgress, achievement.TargetProgress);
			}
			if (currentProgress >= num)
			{
				TryUnlockAchievement(achievement);
			}
		}

		private int GetCurrentProgress(AchievementData achievement)
		{
			if (AchievementManager.Instance == null)
			{
				return 0;
			}
			AchievementManager.AchievementID linkedAchievementID = achievement.LinkedAchievementID;
			return AchievementManager.Instance.GetAchievementProgress(linkedAchievementID);
		}

		private void TryUnlockAchievement(AchievementData achievement)
		{
			if (AchievementManager.Instance != null)
			{
				AchievementManager.AchievementID linkedAchievementID = achievement.LinkedAchievementID;
				if (!AchievementManager.Instance.IsAchievementUnlockedInGameData(linkedAchievementID))
				{
					AchievementManager.Instance.UnlockAchievement(linkedAchievementID);
				}
			}
		}

		private void ShowProgressBar()
		{
			if (progressBarContainer != null)
			{
				progressBarContainer.SetActive(value: true);
			}
		}

		private void HideProgressBar()
		{
			if (progressBarContainer != null)
			{
				progressBarContainer.SetActive(value: false);
			}
		}

		public void Clear()
		{
			titleText.text = "";
			descriptionText.text = "";
			if (iconImage != null)
			{
				iconImage.sprite = null;
			}
			currentAchievement = null;
			HideProgressBar();
		}
	}
}
