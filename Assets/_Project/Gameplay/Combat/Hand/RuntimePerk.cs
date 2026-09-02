using AstralShift.HellMaiden.Data.Perks;

namespace AstralShift.HellMaiden.Combat.Hand
{
	public class RuntimePerk
	{
		public RuntimePerkData RuntimeData { get; private set; }

		public PerkRarity MinRarity => RuntimeData.Data.GetLowestRarity();

		public PerkRarity MaxRarity => RuntimeData.Data.GetHighestRarity();

		public PerkRarity CurrentRarity { get; private set; }

		public int RarityInternalLevel { get; private set; }

		public int Level { get; private set; }

		public bool IsMaxedOut => Level == MaxLevel;

		public bool ReachedMaxRarity => CurrentRarity == MaxRarity;

		public bool ReachedRarityMaxLevel => RarityInternalLevel == PerkData.LevelsPerRarity;

		private int MaxLevel => RuntimeData.Data.GetMaxLevel();

		public RuntimePerk(RuntimePerkData runtimeData)
		{
			RuntimeData = runtimeData;
			CurrentRarity = MinRarity;
			RarityInternalLevel = 1;
			Level = 1;
		}

		public void Upgrade(RuntimePerkData runtimeData)
		{
			if (!IsMaxedOut)
			{
				RarityInternalLevel++;
				Level++;
				if (RarityInternalLevel > PerkData.LevelsPerRarity && !ReachedMaxRarity)
				{
					CurrentRarity++;
					RarityInternalLevel = 1;
				}
				RuntimeData = runtimeData;
			}
		}

		public float GetAtIndexModifierParameterValue(int index)
		{
			float num = 0f;
			PerkRarity minRarity = MinRarity;
			int currentRarity = (int)CurrentRarity;
			for (int i = (int)minRarity; i <= currentRarity; i++)
			{
				PerkRarityModifiersData rarity = RuntimeData.Data.GetRarity((PerkRarity)i);
				num = ((i == currentRarity) ? (num + rarity.Modifiers[index].GetParameterByIndex(0) * (float)RarityInternalLevel) : (num + rarity.Modifiers[index].GetParameterByIndex(0) * (float)PerkData.LevelsPerRarity));
			}
			return num;
		}

	}
}
