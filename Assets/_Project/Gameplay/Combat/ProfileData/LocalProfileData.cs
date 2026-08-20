using AstralShift.DebugTools;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.UI.Menus.Achievement;
using AstralShift.ProfileData;
using Newtonsoft.Json;
using UnityEngine;

namespace AstralShift.HellMaiden.ProfileData
{
	public class LocalProfileData : IProfileAdapter
	{
		public const string ProfileVersionKey = "ProfileVersion";

		public const int CurrentProfileVersion = 1;

		public void SaveAchievements()
		{
		}

		public void LoadAchievements()
		{
		}

		public void ClearAchievements()
		{
			GameData.Instance.AchievementSaveData.Clear();
		}

		public void SaveConfigs()
		{
			PlayerPrefs.SetInt("ProfileVersion", 1);
			PlayerPrefs.SetString("SettingsData", JsonConvert.SerializeObject(SettingsData.Instance));
		}

		public void LoadConfigs()
		{
			PlayerPrefs.GetInt("ProfileVersion", 1);
			_ = 1;
			string value = PlayerPrefs.GetString("SettingsData");
			if (!string.IsNullOrEmpty(value))
			{
				try
				{
					SettingsData.Instance = JsonConvert.DeserializeObject<SettingsData>(value);
				}
				catch (JsonReaderException)
				{
					SettingsData.Instance = new SettingsData();
					DBL.Log(DBL.Module.Settings, "Failed to load settings data (parsing error)! New settings created!", 1);
				}
			}
			else
			{
				SettingsData.Instance = new SettingsData();
			}
			SaveConfigs();
		}

		public void SaveProfileData()
		{
			PlayerPrefs.SetString("ProfileData", JsonConvert.SerializeObject(ProfileData.Instance));
		}

		public void LoadProfileData()
		{
			string value = PlayerPrefs.GetString("ProfileData");
			if (!string.IsNullOrEmpty(value))
			{
				ProfileData.Instance = JsonConvert.DeserializeObject<ProfileData>(value);
			}
			else
			{
				ProfileData.Instance = new ProfileData();
			}
		}

		public void UnlockAchievement(AchievementData achievement)
		{
			SaveAchievements();
			DebugScreenLogger.Instance.Log("Achievement Unlocked :" + achievement.LinkedAchievementID);
		}

		public bool IsAchievementUnlocked(AchievementData achievement)
		{
			return GameDirector.Instance.AchievementManager.IsAchievementUnlockedInGameData(achievement.LinkedAchievementID);
		}

		public void SetAchievementProgress(AchievementData achievement, int progress)
		{
		}
	}
}
