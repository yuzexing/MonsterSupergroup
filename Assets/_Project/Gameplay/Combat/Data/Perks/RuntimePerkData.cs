using System;

namespace AstralShift.HellMaiden.Data.Perks
{
	public class RuntimePerkData : IComparable<RuntimePerkData>, ICloneable
	{
		private PerkData _data;

		public PerkData Data => _data;

		public PerkRarity Rarity { get; private set; }

		public RuntimePerkData(PerkData data, PerkRarity rarity)
		{
			Refresh(data, rarity);
		}

		public void Refresh(PerkData data, PerkRarity rarity)
		{
			_data = data;
			Rarity = rarity;
		}

		public int CompareTo(RuntimePerkData other)
		{
			if (other.Data == Data && other.Rarity == Rarity)
			{
				return 1;
			}
			return 0;
		}

		public object Clone()
		{
			return new RuntimePerkData(Data, Rarity);
		}
	}
}
