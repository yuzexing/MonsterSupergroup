using System;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.UI.Menus.Achievement;
using AstralShift.Helpers.Steam;
using AstralShift.ProfileData;
using Newtonsoft.Json;
// using Steamworks;
using UnityEngine;

namespace AstralShift.HellMaiden.ProfileData
{
	public class SteamProfileData : IProfileAdapter
	{
		public void SaveAchievements()
		{
			if (SteamManager.Initialized)
			{
				// SteamUserStats.StoreStats();
				Debug.LogWarning("Achievements Saved to Steam!");
			}
		}

		public void LoadAchievements()
		{
		}

		public void ClearAchievements()
		{
			if (!SteamManager.Initialized)
			{
				return;
			}
			foreach (AchievementManager.AchievementID value in Enum.GetValues(typeof(AchievementManager.AchievementID)))
			{
				// SteamUserStats.ClearAchievement(value.ToString());
			}
			// SteamUserStats.StoreStats();
			Debug.LogWarning("All Achievements Cleared!");
		}

		public void UnlockAchievement(AchievementData achievement)
		{
			// if (SteamManager.Initialized && !IsAchievementUnlocked(achievement) && SteamUserStats.SetAchievement(achievement.LinkedAchievementID.ToString()))
			// {
			// 	SaveAchievements();
			// }
		}

		public bool IsAchievementUnlocked(AchievementData achievement)
		{
			if (!SteamManager.Initialized)
			{
				return false;
			}
			// if (SteamUserStats.GetAchievement(achievement.LinkedAchievementID.ToString(), out var pbAchieved))
			// {
			// 	return pbAchieved;
			// }
			return false;
		}

		public void SetAchievementProgress(AchievementData achievement, int progress)
		{
			// if (SteamManager.Initialized && SteamUserStats.SetStat(achievement.STEAMprogressID, progress))
			// {
			// 	SaveAchievements();
			// }
		}

		public void LoadConfigs()
		{
			string value = PlayerPrefs.GetString("SettingsData");
			if (!string.IsNullOrEmpty(value))
			{
				SettingsData.Instance = JsonConvert.DeserializeObject<SettingsData>(value);
			}
			else
			{
				SettingsData.Instance = new SettingsData();
			}
		}

		public void SaveConfigs()
		{
			PlayerPrefs.SetString("SettingsData", JsonConvert.SerializeObject(SettingsData.Instance));
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

		public void SaveProfileData()
		{
			PlayerPrefs.SetString("ProfileData", JsonConvert.SerializeObject(ProfileData.Instance));
		}
	}
}
