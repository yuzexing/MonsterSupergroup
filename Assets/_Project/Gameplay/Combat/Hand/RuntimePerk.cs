using System.Collections.Generic;
using AstralShift.HellMaiden.Data.Perks;

namespace AstralShift.HellMaiden.Combat.Hand
{
	public class RuntimePerk
	{
		private List<RuntimePerkModifier> _cachedModifiers;

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
			RuntimePerkModifier[] modifiers = CreateRuntimePerkModifiers();
			StackAndApplyModifiers(modifiers);
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
				RuntimePerkModifier[] modifiers = CreateRuntimePerkModifiers();
				StackAndApplyModifiers(modifiers);
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

		private RuntimePerkModifier[] CreateRuntimePerkModifiers()
		{
			return RuntimeModifierFactory.Instance.GetRuntimeModifiersFromPerkData(RuntimeData.Data, CurrentRarity);
		}

		private void StackAndApplyModifiers(RuntimePerkModifier[] modifiers)
		{
			if (modifiers == null || modifiers.Length == 0)
			{
				return;
			}
			RemoveModifiers();
			if (_cachedModifiers == null)
			{
				_cachedModifiers = new List<RuntimePerkModifier>();
			}
			_cachedModifiers.AddRange(modifiers);
			_cachedModifiers = StackModifiers(_cachedModifiers);
			foreach (RuntimePerkModifier cachedModifier in _cachedModifiers)
			{
				GameDirector.Instance.Player.PlayerStats.AddModifier(cachedModifier);
			}
			GameDirector.Instance.Player.PlayerStats.EvaluateModifiers();
		}

		private void RemoveModifiers()
		{
			if (_cachedModifiers == null || _cachedModifiers.Count == 0)
			{
				return;
			}
			foreach (RuntimePerkModifier cachedModifier in _cachedModifiers)
			{
				GameDirector.Instance.Player.PlayerStats.RemoveModifier(cachedModifier);
			}
		}

		private List<RuntimePerkModifier> StackModifiers(List<RuntimePerkModifier> modifiers)
		{
			List<RuntimePerkModifier> list = new List<RuntimePerkModifier>();
			foreach (RuntimePerkModifier modifier in modifiers)
			{
				bool flag = false;
				foreach (RuntimePerkModifier item in list)
				{
					if (item.TryStack(modifier))
					{
						flag = true;
						break;
					}
				}
				if (!flag)
				{
					list.Add(modifier);
				}
			}
			return list;
		}
	}
}
