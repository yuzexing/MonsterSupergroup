using System;

namespace AstralShift.HellMaiden.GameStats
{
	public abstract class StatEntry
	{
		public event Action onEntryStatsChanged;

		public abstract void CompareHighScores(StatEntry statEntry);

		public abstract void JoinStatsEntries(StatEntry statEntry);

		public abstract void CleanEntry();

		public abstract void CleanLinkedEvents();
	}
}
