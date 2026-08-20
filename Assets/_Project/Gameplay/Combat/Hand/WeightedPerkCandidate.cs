using AstralShift.HellMaiden.Data.Perks;

namespace AstralShift.HellMaiden.Combat.Hand
{
	internal struct WeightedPerkCandidate
	{
		public PerkData Data;

		public PerkRarity Rarity;

		public float Weight;

		public WeightedPerkCandidate(PerkData data, PerkRarity rarity, float weight)
		{
			Data = data;
			Rarity = rarity;
			Weight = weight;
		}
	}
}
