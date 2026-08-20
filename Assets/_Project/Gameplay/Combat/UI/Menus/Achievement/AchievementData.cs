using AstralShift.HellMaiden.Data;
using AstralShift.QTI.Helpers.Attributes;
using UnityEngine;

namespace AstralShift.HellMaiden.UI.Menus.Achievement
{
	[CreateAssetMenu(fileName = "AchievementData", menuName = "HellMaiden/Data/HellMaidenAchievements/AchievementData")]
	public class AchievementData : ScriptableObject
	{
		[Header("Achievement Manager Link")]
		[SerializeField]
		private AchievementManager.AchievementID linkedAchievementID;

		[SerializeField]
		private RarityType rarity;

		[SerializeField]
		private string titleKey;

		[SerializeField]
		private string descriptionKey;

		[SerializeField]
		private Sprite icon;

		[SerializeField]
		private Sprite secretLockedAchievementBackground;

		[SerializeField]
		private Sprite normalUnlockedBackground;

		[SerializeField]
		private Sprite rareUnlockedBackground;

		[SerializeField]
		private bool isSecret;

		[Header("Unlock Conditions")]
		[SerializeField]
		private bool hasProgressToTrack;

		[ConditionalHide("hasProgressToTrack", true)]
		[SerializeField]
		private int targetProgress = 100;

		[ConditionalHide("hasProgressToTrack", true)]
		[Tooltip("Input steam progress stat ID here. Profile can read it if needed through runtime DB")]
		public string STEAMprogressID;

		public AchievementManager.AchievementID LinkedAchievementID => linkedAchievementID;

		public RarityType Rarity => rarity;

		public Sprite Icon => icon;

		public bool HasProgressToTrack => hasProgressToTrack;

		public int TargetProgress => targetProgress;

		public Sprite SecretLockedAchievementBackground => secretLockedAchievementBackground;

		public Sprite NormalUnlockedBackground => normalUnlockedBackground;

		public Sprite RareUnlockedBackground => rareUnlockedBackground;

		public bool IsSecret
		{
			get
			{
				return isSecret;
			}
			set
			{
				isSecret = value;
			}
		}

		public AchievementDisplayState DisplayState
		{
			get
			{
				if (AchievementManager.Instance.IsAchievementUnlockedInGameData(LinkedAchievementID))
				{
					return AchievementDisplayState.Unlocked;
				}
				if (isSecret)
				{
					return AchievementDisplayState.SecretLocked;
				}
				return AchievementDisplayState.NormalLocked;
			}
		}

		public string LocalizedTitle
		{
			get
			{
				if (DisplayState == AchievementDisplayState.SecretLocked)
				{
					return LocalizationMediator.GetTranslation("ACH_HIDDEN_TITLE");
				}
				string translation = LocalizationMediator.GetTranslation(FormatLocalizationKey(((int)(linkedAchievementID + 1)).ToString(), "Title"));
				if (!string.IsNullOrEmpty(translation))
				{
					return translation;
				}
				return titleKey;
			}
		}

		public string LocalizedDescription
		{
			get
			{
				if (DisplayState == AchievementDisplayState.SecretLocked)
				{
					return LocalizationMediator.GetTranslation("ACH_HIDDEN_DESCRIPTION");
				}
				string translation = LocalizationMediator.GetTranslation(FormatLocalizationKey(((int)(linkedAchievementID + 1)).ToString(), "Description"));
				if (!string.IsNullOrEmpty(translation))
				{
					return translation;
				}
				return descriptionKey;
			}
		}

		private string FormatLocalizationKey(string key, string type)
		{
			if (key.StartsWith("ACH_"))
			{
				return key;
			}
			if (int.TryParse(key, out var result))
			{
				if (!(type == "Title"))
				{
					return $"ACH_InfoPanel_Archievement_Description_{result}";
				}
				return $"ACH_InfoPanel_Achievement_Title_{result}";
			}
			return "ACH_" + key;
		}
	}
}
