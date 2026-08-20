using System.Collections.Generic;

namespace AstralShift.HellMaiden.Data.Cards
{
	public class RuntimeCardDataEqualityComparer : IEqualityComparer<RuntimeCardData>
	{
		public bool Equals(RuntimeCardData x, RuntimeCardData y)
		{
			if (x == null || y == null)
			{
				return false;
			}
			if (x.BaseData == y.BaseData)
			{
				return x.LevelIndex == y.LevelIndex;
			}
			return false;
		}

		public int GetHashCode(RuntimeCardData obj)
		{
			return obj.BaseData.GetHashCode();
		}
	}
}
