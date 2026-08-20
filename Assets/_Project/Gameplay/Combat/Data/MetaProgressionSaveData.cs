using System.Collections.Generic;
using Assets.Scripts.AstralShift.HellMaiden.Data;

namespace AstralShift.HellMaiden.Data
{
	public class MetaProgressionSaveData
	{
		public Dictionary<MetaProgressionID, int> unlockedLevels;

		public MetaProgressionSaveData()
		{
			unlockedLevels = new Dictionary<MetaProgressionID, int>();
		}
	}
}
