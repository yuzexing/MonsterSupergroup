using System.Collections.Generic;
using System.Linq;

namespace AstralShift.HellMaiden.GameStats
{
	public class GameStatsTracker
	{
		public int totalCurrencySpent;

		private Dictionary<uint, WeaponStatsEntry> WeaponStatsEntries { get; set; } = new Dictionary<uint, WeaponStatsEntry>();

		public PlayerStatsEntry CummulativePlayerStatsEntry { get; private set; }

		private Dictionary<uint, WeaponStatsEntry> HighScoreWeaponStatsEntries { get; set; } = new Dictionary<uint, WeaponStatsEntry>();

		public PlayerStatsEntry HighScorePlayerStatsEntry { get; private set; }

		public GameStatsTracker()
		{
			CummulativePlayerStatsEntry = CummulativePlayerStatsEntry ?? new PlayerStatsEntry();
			HighScorePlayerStatsEntry = HighScorePlayerStatsEntry ?? new PlayerStatsEntry();
			WeaponStatsEntries = WeaponStatsEntries ?? new Dictionary<uint, WeaponStatsEntry>();
			HighScoreWeaponStatsEntries = HighScoreWeaponStatsEntries ?? new Dictionary<uint, WeaponStatsEntry>();
		}

		public void SaveEndOfRunStats(RunStatsTracker runStatsEntry)
		{
			if (CummulativePlayerStatsEntry == null)
			{
				CummulativePlayerStatsEntry = new PlayerStatsEntry();
				HighScorePlayerStatsEntry = new PlayerStatsEntry();
			}
			CummulativePlayerStatsEntry.JoinStatsEntries(runStatsEntry.PlayerStatsEntry);
			HighScorePlayerStatsEntry.CompareHighScores(runStatsEntry.PlayerStatsEntry);
			for (int i = 0; i < runStatsEntry.WeaponStatsEntries.Count; i++)
			{
				uint key = runStatsEntry.WeaponStatsEntries.ElementAt(i).Key;
				if (!WeaponStatsEntries.ContainsKey(key))
				{
					WeaponStatsEntries.Add(key, new WeaponStatsEntry());
					HighScoreWeaponStatsEntries.Add(key, new WeaponStatsEntry());
				}
				WeaponStatsEntries[key].JoinStatsEntries(runStatsEntry.WeaponStatsEntries.ElementAt(i).Value);
				HighScoreWeaponStatsEntries[key].CompareHighScores(runStatsEntry.WeaponStatsEntries.ElementAt(i).Value);
			}
		}
	}
}
