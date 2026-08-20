using System;
using System.Collections.Generic;
using System.Linq;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Data.Perks;
using AstralShift.HellMaiden.GameStats;
using UnityEngine;

namespace AstralShift.HellMaiden.Data.Achievements
{
	public static class AchievementVerifier
	{
		private static Action[] CircleVerificationIndex = new Action[2] { LimboVerifications, LustVerifications };

		private static int CircleHighScore = 100000;

		public static void VerifyAchievements()
		{
			Debug.Log("Verifying end of run achievements");
			VerifyGenericEndRunAchievements();
			int circle = RunStatsTracker.Instance.Circle;
			try
			{
				CircleVerificationIndex[circle - 1]();
			}
			catch (IndexOutOfRangeException ex)
			{
				Debug.LogWarning("ACHIEVEMENT CODE, ALWAYS ERROR IF PLAYING DIRECTLY IN A BOSS SCENE: " + ex);
			}
		}

		private static void VerifyGenericEndRunAchievements()
		{
			if (verifyPlayerLVL(50))
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.InfernalProwessI);
			}
			if (VerifyHasCrystalCharm())
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.HeavenlyGift);
			}
			if (VerifyAnyWeaponDamage(200000))
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.DevilishTalent);
			}
			if (RunStatsTracker.Instance.WeaponStatsEntries.Count == 4)
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.WeaponMaster);
			}
			if (VerifyFullHand())
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.StrategyMaster);
			}
			if (VerifyAllPoetWeaponsEquipped(PoetPoolID.Dante))
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.SoulOfPoetI);
			}
			if (VerifyAllPoetEquipmentsEquipped(PoetPoolID.Dante))
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.EssenceOfPoetI);
			}
			if (VerifyAllPoetWeaponsEquipped(PoetPoolID.Virgil))
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.SoulOfPoetII);
			}
			if (VerifyAllPoetEquipmentsEquipped(PoetPoolID.Virgil))
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.EssenceOfPoetII);
			}
			if (VerifyAllPoetWeaponsEquipped(PoetPoolID.Horace))
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.SoulOfPoetIII);
			}
			if (VerifyAllPoetEquipmentsEquipped(PoetPoolID.Horace))
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.EssenceOfPoetIII);
			}
			if (VerifyAllPoetWeaponsEquipped(PoetPoolID.Homer))
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.SoulOfPoetIV);
			}
			if (VerifyAllPoetEquipmentsEquipped(PoetPoolID.Homer))
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.EssenceOfPoetIV);
			}
			if (VerifyAllPoetWeaponsEquipped(PoetPoolID.Ovid))
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.SoulOfPoetV);
			}
			if (VerifyAllPoetEquipmentsEquipped(PoetPoolID.Ovid))
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.EssenceOfPoetV);
			}
			if (VerifyAnyWeaponFullLVL3())
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.ModMaster);
			}
			if (VerifyFullHandLVL3())
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.PerfectHand);
			}
			if (RunStatsTracker.Instance.PlayerStatsEntry.totalEnemiesDefeated >= 2500)
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.Bloodthirsty);
			}
			if (GameData.Instance.GameStatsTracker.CummulativePlayerStatsEntry.shrineActivationCount.Count >= 6)
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.DevoutPilgrim);
			}
			if (RunStatsTracker.Instance.PlayerStatsEntry.shrinesActivated >= 12)
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.PiousPilgrim);
			}
			if (RunStatsTracker.Instance.SignatureWeapon == 100)
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.WayfinderI);
				if (RunStatsTracker.Instance.PlayerStatsEntry.ultimatesUsed > 0)
				{
					AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.WayfinderII);
				}
			}
			if (RunStatsTracker.Instance.SignatureWeapon == 200)
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.CarpeDiemI);
				if (RunStatsTracker.Instance.PlayerStatsEntry.ultimatesUsed > 0)
				{
					AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.CarpeDiemII);
				}
			}
			if (RunStatsTracker.Instance.SignatureWeapon == 301)
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.WingedLyricI);
				if (RunStatsTracker.Instance.PlayerStatsEntry.ultimatesUsed > 0)
				{
					AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.WingedLyricII);
				}
			}
			if (RunStatsTracker.Instance.SignatureWeapon == 401)
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.CodexAmorisI);
				if (RunStatsTracker.Instance.PlayerStatsEntry.ultimatesUsed > 0)
				{
					AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.CodexAmorisII);
				}
			}
			if (RunStatsTracker.Instance.SignatureWeapon == 1 && RunStatsTracker.Instance.PlayerStatsEntry.ultimatesUsed > 0)
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.BlazingQuillI);
			}
		}

		private static void LimboVerifications()
		{
			int num = 1;
			if (GameDataManager.GetGameTriggerState("KillMinos"))
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.SoulMagister);
			}
			if (GameDataManager.GetGameTriggerState("SaveHorace"))
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.GoldenSatirist);
			}
			if (GameDataManager.GetGameTriggerState("SaveHomer"))
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.CrystalEyedBard);
			}
			if (VerifyRunTime(720))
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.ConquererOfLimbo);
			}
			if (VerifyNumber("Number_MinosKillCount", 3))
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.TabooArtsI);
			}
			if (!AchievementManager.Instance.IsAchievementUnlockedInGameData(AchievementManager.AchievementID.BrotchiHunter))
			{
				AchievementManager.Instance.IncrementAchievementProgress(AchievementManager.AchievementID.BrotchiHunter, GetSpecificEnemyKillCount("Brotchi"));
			}
			if (RunStatsTracker.Instance.PlayerStatsEntry.scores[num] > CircleHighScore)
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.HellishMasteryI);
			}
			if (VerifySignatureWeaponRunCompletion(num, PoetPoolID.Virgil))
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.WayfinderIII);
			}
			if (!AchievementManager.Instance.IsAchievementUnlockedInGameData(AchievementManager.AchievementID.SkeletonHunter))
			{
				AchievementManager.Instance.IncrementAchievementProgress(AchievementManager.AchievementID.SkeletonHunter, GetSpecificEnemyKillCount("Skeleton"));
			}
			if (!AchievementManager.Instance.IsAchievementUnlockedInGameData(AchievementManager.AchievementID.GhoulHunter))
			{
				AchievementManager.Instance.IncrementAchievementProgress(AchievementManager.AchievementID.GhoulHunter, GetSpecificEnemyKillCount("Ghoul"));
			}
			if (!AchievementManager.Instance.IsAchievementUnlockedInGameData(AchievementManager.AchievementID.ImpHunter))
			{
				AchievementManager.Instance.IncrementAchievementProgress(AchievementManager.AchievementID.ImpHunter, GetSpecificEnemyKillCount("Imp"));
			}
			if (!AchievementManager.Instance.IsAchievementUnlockedInGameData(AchievementManager.AchievementID.LostSoulHunter))
			{
				AchievementManager.Instance.IncrementAchievementProgress(AchievementManager.AchievementID.LostSoulHunter, GetSpecificEnemyKillCount("LostSoul"));
			}
			if (!AchievementManager.Instance.IsAchievementUnlockedInGameData(AchievementManager.AchievementID.SlimeHunter))
			{
				AchievementManager.Instance.IncrementAchievementProgress(AchievementManager.AchievementID.SlimeHunter, GetSpecificEnemyKillCount("Slime"));
			}
			if (VerifySignatureWeaponRunCompletion(num, PoetPoolID.Horace))
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.CarpeDiemIII);
			}
			if (VerifySignatureWeaponRunCompletion(num, PoetPoolID.Homer))
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.WingedLyricIII);
			}
			if (VerifySignatureWeaponRunCompletion(num, PoetPoolID.Dante))
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.BlazingQuillII);
			}
			if (RunStatsTracker.Instance.RunSucessfull && RunStatsTracker.Instance.PlayerStatsEntry.healthRecovered <= 500)
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.VitalEnduranceI);
			}
		}

		private static void LustVerifications()
		{
			int num = 2;
			if (VerifyRunTime(720))
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.ConquererOfLust);
			}
			if (RunStatsTracker.Instance.RunSucessfull)
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.MalebranchePunisher);
			}
			if (GameDataManager.HasCutscenePlayed("CUT_C2_SCA_MIDBOSS_POST"))
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.SalamanderInquisitor);
			}
			if (GameDataManager.GetGameTriggerState("SaveOvid"))
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.CeruleanImago);
			}
			if (VerifyNumber("Number_ScarmiLibiKillCount", 3))
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.TabooArtsII);
			}
			if (RunStatsTracker.Instance.PlayerStatsEntry.scores[num] > CircleHighScore)
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.HellishMasteryII);
			}
			if (RunStatsTracker.Instance.RunSucessfull && RunStatsTracker.Instance.PlayerStatsEntry.healthRecovered <= 500)
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.VitalEnduranceII);
			}
			if (!AchievementManager.Instance.IsAchievementUnlockedInGameData(AchievementManager.AchievementID.SuccubiHunter))
			{
				AchievementManager.Instance.IncrementAchievementProgress(AchievementManager.AchievementID.SuccubiHunter, GetSpecificEnemyKillCount("Succubus"));
			}
			if (!AchievementManager.Instance.IsAchievementUnlockedInGameData(AchievementManager.AchievementID.EliteKillerI) && GetSpecificEnemyKillCount("Elite_Viper") > 0)
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.EliteKillerI);
			}
			if (VerifySignatureWeaponRunCompletion(num, PoetPoolID.Ovid))
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.CodexAmorisIII);
			}
			if (VerifySignatureWeaponRunCompletion(num, PoetPoolID.Dante))
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.BlazingQuillIII);
			}
			if (!AchievementManager.Instance.IsAchievementUnlockedInGameData(AchievementManager.AchievementID.EliteKillerII) && GetSpecificEnemyKillCount("Succubus Elite") > 0)
			{
				AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.EliteKillerII);
			}
			if (!AchievementManager.Instance.IsAchievementUnlockedInGameData(AchievementManager.AchievementID.LustSinnerHunter))
			{
				AchievementManager.Instance.IncrementAchievementProgress(AchievementManager.AchievementID.LustSinnerHunter, GetSpecificEnemyKillCount("LustSinner"));
			}
			if (!AchievementManager.Instance.IsAchievementUnlockedInGameData(AchievementManager.AchievementID.LustMineHunter))
			{
				AchievementManager.Instance.IncrementAchievementProgress(AchievementManager.AchievementID.LustMineHunter, GetSpecificEnemyKillCount("LustMine"));
			}
			if (!AchievementManager.Instance.IsAchievementUnlockedInGameData(AchievementManager.AchievementID.ViperHunter))
			{
				AchievementManager.Instance.IncrementAchievementProgress(AchievementManager.AchievementID.ViperHunter, GetSpecificEnemyKillCount("Viper"));
			}
			if (!AchievementManager.Instance.IsAchievementUnlockedInGameData(AchievementManager.AchievementID.DeviBrotchiHunter))
			{
				AchievementManager.Instance.IncrementAchievementProgress(AchievementManager.AchievementID.DeviBrotchiHunter, GetSpecificEnemyKillCount("Flying Brotchi"));
			}
		}

		private static bool VerifyRunTime(int time)
		{
			return RunStatsTracker.Instance.PlayerStatsEntry.TotalTimeSurvived >= (float)time;
		}

		private static bool VerifyNumber(string numberID, int number)
		{
			return GameDataManager.GetGameInt(numberID) >= number;
		}

		private static bool VerifySignatureWeaponRunCompletion(int circle, PoetPoolID poet)
		{
			if (!RunStatsTracker.Instance.RunSucessfull)
			{
				return false;
			}
			return poet switch
			{
				PoetPoolID.Dante => RunStatsTracker.Instance.SignatureWeapon == 1, 
				PoetPoolID.Virgil => RunStatsTracker.Instance.SignatureWeapon == 100, 
				PoetPoolID.Horace => RunStatsTracker.Instance.SignatureWeapon == 200, 
				PoetPoolID.Homer => RunStatsTracker.Instance.SignatureWeapon == 301, 
				PoetPoolID.Ovid => RunStatsTracker.Instance.SignatureWeapon == 401, 
				PoetPoolID.Lucan => RunStatsTracker.Instance.SignatureWeapon == 501, 
				_ => false, 
			};
		}

		private static int GetSpecificEnemyKillCount(string enemyID)
		{
			int value = 0;
			RunStatsTracker.Instance.PlayerStatsEntry.enemiesDefeated.TryGetValue(enemyID, out value);
			return value;
		}

		private static bool verifyPlayerLVL(int lvl)
		{
			return RunStatsTracker.Instance.PlayerStatsEntry.maxLevelReached >= (float)lvl;
		}

		private static bool VerifyAnyWeaponDamage(int damage)
		{
			foreach (KeyValuePair<uint, WeaponStatsEntry> weaponStatsEntry in RunStatsTracker.Instance.WeaponStatsEntries)
			{
				if (weaponStatsEntry.Value.TotalDamage >= (float)damage)
				{
					return true;
				}
			}
			return false;
		}

		private static bool VerifyFullHand()
		{
			foreach (PlayerHandSlot slot in PlayerHand.Instance.Slots)
			{
				if (!slot.HasWeapon())
				{
					return false;
				}
				if (slot.Equipments.Count != 3)
				{
					return false;
				}
			}
			return true;
		}

		private static bool VerifyFullHandLVL3()
		{
			foreach (PlayerHandSlot slot in PlayerHand.Instance.Slots)
			{
				if (!slot.HasWeapon())
				{
					return false;
				}
				if (slot.Equipments.Count != 3)
				{
					return false;
				}
				foreach (RuntimeEquipmentData equipment in slot.Equipments)
				{
					if (!equipment.IsMaxLevel())
					{
						return false;
					}
				}
			}
			return true;
		}

		private static bool VerifyAnyWeaponFullLVL3()
		{
			foreach (PlayerHandSlot slot in PlayerHand.Instance.Slots)
			{
				if (!slot.HasWeapon() || slot.Equipments.Count != 3)
				{
					continue;
				}
				int num = 0;
				foreach (RuntimeEquipmentData equipment in slot.Equipments)
				{
					if (equipment.IsMaxLevel())
					{
						num++;
					}
				}
				if (num == 3)
				{
					return true;
				}
			}
			return false;
		}

		private static bool VerifyAllPoetWeaponsEquipped(PoetPoolID poet)
		{
			List<uint> list = (from w in GameDirector.Instance.runtimeDB.WeaponDB.Weapons
				where w.poolID == poet
				select w.ID).ToList();
			for (int num = 0; num < list.Count; num++)
			{
				if (!GameData.Instance.GameStatsTracker.CummulativePlayerStatsEntry.weaponCounts.ContainsKey(list[num]))
				{
					return false;
				}
			}
			return true;
		}

		private static bool VerifyAllPoetEquipmentsEquipped(PoetPoolID poet)
		{
			List<uint> list = (from e in GameDirector.Instance.runtimeDB.EquipmentDB.Equipments
				where e.poolID == poet
				select e.ID).ToList();
			for (int num = 0; num < list.Count; num++)
			{
				if (!GameData.Instance.GameStatsTracker.CummulativePlayerStatsEntry.equipmentCounts.ContainsKey(list[num]))
				{
					return false;
				}
			}
			return true;
		}

		private static bool VerifyHasFullyLeveledCharm()
		{
			foreach (RuntimePerk perks in PlayerHand.Instance.PerksList)
			{
				if (perks.Level == 9)
				{
					return true;
				}
			}
			return false;
		}

		private static bool VerifyHasCrystalCharm()
		{
			foreach (RuntimePerk perks in PlayerHand.Instance.PerksList)
			{
				if (perks.MinRarity == PerkRarity.Crystal)
				{
					return true;
				}
			}
			return false;
		}
	}
}
