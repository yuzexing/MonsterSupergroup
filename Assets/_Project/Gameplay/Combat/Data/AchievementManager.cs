using System;
using AstralShift.HellMaiden.UI.Menus.Achievement;
using AstralShift.ProfileData;
using UnityEngine;

namespace AstralShift.HellMaiden.Data
{
	public class AchievementManager
	{
		public enum AchievementID
		{
			SoulMagister = 0,
			GoldenSatirist = 1,
			CrystalEyedBard = 2,
			AstralWayfarer = 3,
			ConquererOfLimbo = 4,
			ConquererOfLust = 5,
			MalebranchePunisher = 6,
			SalamanderInquisitor = 7,
			CeruleanImago = 8,
			TabooArtsI = 9,
			TabooArtsII = 10,
			BrotchiHunter = 11,
			InfernalProwessI = 12,
			InfernalProwessII = 13,
			HeavenlyProwessI = 14,
			HeavenlyGift = 15,
			DevilishTalent = 16,
			HellishMasteryI = 17,
			HellishMasteryII = 18,
			WeaponMaster = 19,
			StrategyMaster = 20,
			SoulOfPoetI = 21,
			EssenceOfPoetI = 22,
			SoulOfPoetII = 23,
			EssenceOfPoetII = 24,
			SoulOfPoetIII = 25,
			EssenceOfPoetIII = 26,
			SoulOfPoetIV = 27,
			EssenceOfPoetIV = 28,
			SoulOfPoetV = 29,
			EssenceOfPoetV = 30,
			ModMaster = 31,
			PerfectHand = 32,
			Bloodthirsty = 33,
			DevoutPilgrim = 34,
			PiousPilgrim = 35,
			VitalEnduranceI = 36,
			VitalEnduranceII = 37,
			WayfinderI = 38,
			WayfinderII = 39,
			SuccubiHunter = 40,
			WayfinderIII = 41,
			EliteKillerI = 42,
			EliteVaporizer = 43,
			SkeletonHunter = 44,
			GhoulHunter = 45,
			ImpHunter = 46,
			LostSoulHunter = 47,
			SlimeHunter = 48,
			CarpeDiemI = 49,
			CarpeDiemII = 50,
			CarpeDiemIII = 51,
			WingedLyricI = 52,
			WingedLyricII = 53,
			WingedLyricIII = 54,
			CodexAmorisI = 55,
			CodexAmorisII = 56,
			CodexAmorisIII = 57,
			BlazingQuillII = 58,
			BlazingQuillIII = 59,
			BlazingQuillI = 60,
			EliteKillerII = 61,
			LustSinnerHunter = 62,
			LustMineHunter = 63,
			FairyHunter = 64,
			ViperHunter = 65,
			DeviBrotchiHunter = 66,
			WindsOfDesire = 67,
			AbyssalSpender = 68,
			PilgrimsJourneyI = 69,
			PoetsOfLimboI = 70,
			PoetsOfLimboII = 71,
			PowerlessQueen = 72,
			ClimbingTheLadder = 73,
			ForumDweller = 74,
			NovicePoet = 75
		}

		public static AchievementManager Instance { get; private set; }

		public event Action<AchievementID, int> OnProgressChanged;

		public event Action<AchievementID> OnAchievementUnlocked;

		public AchievementManager()
		{
			Instance = this;
		}

		public void UnlockAchievement(AchievementID id)
		{
			if (IsAchievementUnlockedInGameData(id))
			{
				Debug.Log($"Achievement {id} already unlocked");
				return;
			}
			ProfileDataManager.UnlockAchievement(GameDirector.Instance.runtimeDB.GetAchievement(id));
			UnlockLocalAchievement(id);
			Debug.Log($"Achievement unlocked: {id}");
		}

		public void UnlockLocalAchievement(AchievementID id)
		{
			if (IsAchievementUnlockedInGameData(id))
			{
				Debug.Log($"Achievement {id} already unlocked");
				return;
			}
			AddAchievementToGameData(id);
			this.OnAchievementUnlocked?.Invoke(id);
		}

		public void IncrementAchievementProgress(AchievementID id, int progressIncrement)
		{
			int num = 0;
			if (GameData.Instance.AchievementSaveData.ContainsKey(id))
			{
				num = GameData.Instance.AchievementSaveData[id] + progressIncrement;
				GameData.Instance.AchievementSaveData[id] = num;
			}
			else
			{
				GameData.Instance.AchievementSaveData.Add(id, progressIncrement);
			}
			AchievementData achievement = GameDirector.Instance.runtimeDB.GetAchievement(id);
			if (achievement != null && achievement.HasProgressToTrack && num >= achievement.TargetProgress)
			{
				UnlockAchievement(id);
			}
			SetPlatformAchievementProgress(id, num);
			this.OnProgressChanged?.Invoke(id, num);
		}

		public void SetPlatformAchievementProgress(AchievementID id, int progress)
		{
			ProfileDataManager.SetAchievementProgress(GameDirector.Instance.runtimeDB.GetAchievement(id), progress);
		}

		private void AddAchievementToGameData(AchievementID id, int progress = 1)
		{
			if (!GameData.Instance.AchievementSaveData.ContainsKey(id))
			{
				GameData.Instance.AchievementSaveData.Add(id, progress);
			}
		}

		public bool IsAchievementUnlockedInPlatform(AchievementID id)
		{
			return ProfileDataManager.IsAchievementUnlocked(GameDirector.Instance.runtimeDB.GetAchievement(id));
		}

		public bool IsAchievementUnlockedInGameData(AchievementID id)
		{
			AchievementData achievement = GameDirector.Instance.runtimeDB.GetAchievement(id);
			return IsAchievementUnlockedInGameData(achievement);
		}

		public bool IsAchievementUnlockedInGameData(AchievementData achievement)
		{
			if (GameData.Instance.AchievementSaveData.TryGetValue(achievement.LinkedAchievementID, out var value))
			{
				if (achievement != null && achievement.HasProgressToTrack)
				{
					return value >= achievement.TargetProgress;
				}
				return true;
			}
			return false;
		}

		public int GetAchievementProgress(AchievementID id)
		{
			if (GameData.Instance.AchievementSaveData.TryGetValue(id, out var value))
			{
				return value;
			}
			return 0;
		}

		public int GetRequiredProgress(AchievementID id)
		{
			AchievementData achievement = GameDirector.Instance.runtimeDB.GetAchievement(id);
			if (achievement != null && achievement.HasProgressToTrack)
			{
				return achievement.TargetProgress;
			}
			return 1;
		}
	}
}
