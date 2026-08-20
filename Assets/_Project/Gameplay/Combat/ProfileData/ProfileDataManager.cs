using AstralShift.HellMaiden.UI.Menus.Achievement;

namespace AstralShift.ProfileData
{
	public class ProfileDataManager
	{
		public static IProfileAdapter Adapter { get; set; }

		public static void SaveConfigs()
		{
			Adapter.SaveConfigs();
		}

		public static void SaveAchievements()
		{
			Adapter.SaveAchievements();
		}

		public static void SaveProfileData()
		{
			Adapter.SaveProfileData();
		}

		public static void LoadConfigs()
		{
			Adapter.LoadConfigs();
		}

		public static void LoadAchievements()
		{
			Adapter.LoadAchievements();
		}

		public static void LoadProfileData()
		{
			Adapter.LoadProfileData();
		}

		public static void ClearAchievements()
		{
			Adapter.ClearAchievements();
		}

		public static void UnlockAchievement(AchievementData achievement)
		{
			Adapter.UnlockAchievement(achievement);
		}

		public static bool IsAchievementUnlocked(AchievementData achievement)
		{
			return Adapter.IsAchievementUnlocked(achievement);
		}

		public static void SetAchievementProgress(AchievementData achievement, int progress)
		{
			Adapter.SetAchievementProgress(achievement, progress);
		}
	}
}
