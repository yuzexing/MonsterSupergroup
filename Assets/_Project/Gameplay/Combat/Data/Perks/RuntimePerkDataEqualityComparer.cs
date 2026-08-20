using System.Collections.Generic;

namespace AstralShift.HellMaiden.Data.Perks
{
	public class RuntimePerkDataEqualityComparer : IEqualityComparer<RuntimePerkData>
	{
		public bool Equals(RuntimePerkData x, RuntimePerkData y)
		{
			if (x == null || y == null)
			{
				return false;
			}
			if (x.Data == y.Data)
			{
				return x.Rarity == y.Rarity;
			}
			return false;
		}

		public int GetHashCode(RuntimePerkData obj)
		{
			return obj.Data.GetHashCode();
		}
	}
}
