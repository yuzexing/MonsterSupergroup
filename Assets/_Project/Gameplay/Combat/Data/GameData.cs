using System.Collections.Generic;
using AstralShift.HellMaiden.Dialogue;
using AstralShift.HellMaiden.GameStats;
using PixelCrushers;

namespace AstralShift.HellMaiden.Data
{
	public class GameData
	{
		private static GameData instance;

		public int SaveVersion;

		public uint signatureWeaponID = 1u;

		public List<string> viewedDialogues;

		public List<string> viewedCutscenes;

		public SavedGameData DialogueSystemSavedGameData;

		public List<PoetPoolID> unlockedPoets;

		public List<PoetID> availableHubNPCs;

		public MetaProgressionSaveData MetaProgressionSaveData;

		public GameStatsTracker GameStatsTracker;

		public int currency;

		public Dictionary<AchievementManager.AchievementID, int> AchievementSaveData;

		public static GameData Instance
		{
			get
			{
				if (instance == null)
				{
					instance = new GameData();
				}
				return instance;
			}
			set
			{
				instance = value;
			}
		}

		public GameData()
		{
			viewedDialogues = viewedDialogues ?? new List<string>();
			viewedCutscenes = viewedCutscenes ?? new List<string>();
			unlockedPoets = unlockedPoets ?? new List<PoetPoolID>();
			availableHubNPCs = availableHubNPCs ?? new List<PoetID>();
			MetaProgressionSaveData = MetaProgressionSaveData ?? new MetaProgressionSaveData();
			GameStatsTracker = GameStatsTracker ?? new GameStatsTracker();
			AchievementSaveData = AchievementSaveData ?? new Dictionary<AchievementManager.AchievementID, int>();
		}

		public void Reset()
		{
			instance = new GameData();
		}
	}
}
