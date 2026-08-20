using AstralShift.HellMaiden.UI.Menus.Achievement;

namespace AstralShift.ProfileData
{
	public interface IProfileAdapter
	{
		void SaveConfigs();

		void LoadConfigs();

		void SaveProfileData();

		void LoadProfileData();

		void SaveAchievements();

		void LoadAchievements();

		void ClearAchievements();

		void UnlockAchievement(AchievementData achievement);

		void SetAchievementProgress(AchievementData achievement, int progress);

		bool IsAchievementUnlocked(AchievementData achievement);
	}
}
