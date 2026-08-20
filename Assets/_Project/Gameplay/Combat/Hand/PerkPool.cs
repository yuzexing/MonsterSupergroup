using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Data.Perks;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Hand
{
	public class PerkPool
	{
		private Dictionary<PerkPoolID, List<PerkData>> _perkPools;

		private PerkDropWeightsData _perkDropWeightsData;

		private PerkDropPerLevelThreshold _currentLevelDrops;

		private int _currentLevelThresholdIndex;

		private HashSet<PerkDropTier> _allowedTiers;

		private Dictionary<PerkPoolID, List<PerkData>> _bannedPerksData;

		private const int MaxPerksPerChoice = 3;

		private const PerkPoolID PoolID = PerkPoolID.Beatrice;

		public PerkDropWeightsData PerkDropWeights => _perkDropWeightsData;

		public void Init()
		{
			_perkPools = GameDirector.Instance.runtimeDB.GetPerkPoolData();
			_perkDropWeightsData = GameDirector.Instance.runtimeDB.PerkDropWeightsData;
			_currentLevelThresholdIndex = 0;
			_currentLevelDrops = PerkDropWeights.GetPerLevelThresholdDrop(_currentLevelThresholdIndex);
			_allowedTiers = new HashSet<PerkDropTier>();
			PerkDropTier[] array = Enum.GetValues(typeof(PerkDropTier)).Cast<PerkDropTier>().ToArray();
			for (int i = 0; i < array.Length; i++)
			{
				_allowedTiers.Add(array[i]);
			}
			_bannedPerksData = new Dictionary<PerkPoolID, List<PerkData>>();
		}

		public RuntimePerkData[] GetNewPerksDrop(out PerkDropTier tier)
		{
			if (!_perkPools.ContainsKey(PerkPoolID.Beatrice))
			{
				Debug.LogError("PERK POOL: INVALID PERK POOL");
				tier = PerkDropTier.Basic;
				return Array.Empty<RuntimePerkData>();
			}
			HashSet<PerkDropTier> hashSet = new HashSet<PerkDropTier>(_allowedTiers);
			while (hashSet.Count > 0)
			{
				PerkDropTierWeights weightRandomDropTier = GetWeightRandomDropTier(_currentLevelDrops.Drop, hashSet);
				if (IsTheDropTierValid(weightRandomDropTier))
				{
					RuntimePerkData[] randomPerkDataArray = GetRandomPerkDataArray(_perkPools[PerkPoolID.Beatrice], weightRandomDropTier);
					tier = ((randomPerkDataArray.Length != 0) ? GetBestFitTier(randomPerkDataArray) : weightRandomDropTier.Tier);
					return randomPerkDataArray;
				}
				Debug.LogWarning($"PERK POOL: Tier {weightRandomDropTier.Tier} has no valid perks for current progression. Skipping.");
				hashSet.Remove(weightRandomDropTier.Tier);
			}
			Debug.LogWarning("PERK POOL: Logical Deadlock. Forcing fallback to the first Tier definition.");
			PerkDropTierWeights dropTierWeights = _currentLevelDrops.Drop[0];
			RuntimePerkData[] randomPerkDataArray2 = GetRandomPerkDataArray(_perkPools[PerkPoolID.Beatrice], dropTierWeights);
			tier = ((randomPerkDataArray2.Length != 0) ? GetBestFitTier(randomPerkDataArray2) : dropTierWeights.Tier);
			return randomPerkDataArray2;
		}

		public RuntimePerkData[] GetNewPerksDrop(PerkDropTier tier)
		{
			if (!_perkPools.ContainsKey(PerkPoolID.Beatrice))
			{
				Debug.LogError("PERK POOL: INVALID PERK POOL");
				return Array.Empty<RuntimePerkData>();
			}
			PerkDropTierWeights dropTierWeights = _currentLevelDrops.Drop.FirstOrDefault((PerkDropTierWeights perkDropTier) => perkDropTier.Tier == tier);
			return GetRandomPerkDataArray(_perkPools[PerkPoolID.Beatrice], dropTierWeights);
		}

		private PerkDropTierWeights GetWeightRandomDropTier(PerkDropTierWeights[] tiers, HashSet<PerkDropTier> allowedTiers)
		{
			List<PerkDropTierWeights> list = new List<PerkDropTierWeights>();
			if (tiers != null)
			{
				for (int i = 0; i < tiers.Length; i++)
				{
					PerkDropTierWeights item = tiers[i];
					if (allowedTiers.Contains(item.Tier))
					{
						list.Add(item);
					}
				}
			}
			if (list.Count == 0)
			{
				return default(PerkDropTierWeights);
			}
			float num = 0f;
			foreach (PerkDropTierWeights item2 in list)
			{
				num += item2.Weight;
			}
			float num2 = UnityEngine.Random.Range(0f, num);
			foreach (PerkDropTierWeights item3 in list)
			{
				if (num2 <= item3.Weight)
				{
					return item3;
				}
				num2 -= item3.Weight;
			}
			return list[0];
		}

		private bool IsTheDropTierValid(PerkDropTierWeights tierWeights)
		{
			List<PerkData> poolList = _perkPools[PerkPoolID.Beatrice];
			return (from perk in GetPerksPerRarityMap(poolList, tierWeights.SupportedRarities).Values.SelectMany((List<PerkData> list) => list)
				select perk.ID).Distinct().Count() >= 3;
		}

		private RuntimePerkData[] GetRandomPerkDataArray(List<PerkData> poolList, PerkDropTierWeights dropTierWeights)
		{
			List<RuntimePerkData> list = new List<RuntimePerkData>();
			List<PerkData> list2 = new List<PerkData>(poolList);
			for (int i = 0; i < 3; i++)
			{
				RuntimePerkData perkData = GetRandomPerkData(list2, ref dropTierWeights);
				if (perkData == null)
				{
					break;
				}
				list.Add(perkData);
				list2.RemoveAll((PerkData element) => element.ID == perkData.Data.ID);
			}
			return list.ToArray();
		}

		private RuntimePerkData GetRandomPerkData(List<PerkData> poolList, ref PerkDropTierWeights tierWeights)
		{
			if (poolList == null || poolList.Count == 0)
			{
				return null;
			}
			List<PerkData> list = new List<PerkData>(poolList);
			list.RemoveAll((PerkData perk) => !AreDependenciesMet(perk, Leveler.Instance.CardPool.ChosenEquipmentTypes));
			HashSet<PerkRarity> supportedRarities = tierWeights.SupportedRarities;
			FilterPerksBySupportedRarities(list, supportedRarities);
			if (list.Count == 0)
			{
				return null;
			}
			Dictionary<PerkRarity, List<PerkData>> perksPerRarityMap = GetPerksPerRarityMap(list, supportedRarities);
			List<WeightedPerkCandidate> list2 = new List<WeightedPerkCandidate>();
			float num = 0f;
			foreach (PerkRarityWeight rarityWeight in tierWeights.RarityWeights)
			{
				PerkRarity rarity = rarityWeight.Rarity;
				if (!perksPerRarityMap.ContainsKey(rarity) || perksPerRarityMap[rarity].Count == 0)
				{
					continue;
				}
				List<PerkData> list3 = perksPerRarityMap[rarity];
				float num2 = rarityWeight.Weight / (float)list3.Count;
				foreach (PerkData item in list3)
				{
					float num3 = num2 * item.poolWeight;
					list2.Add(new WeightedPerkCandidate(item, rarity, num3));
					num += num3;
				}
			}
			if (list2.Count == 0)
			{
				return null;
			}
			float num4 = UnityEngine.Random.Range(0f, num);
			for (int num5 = 0; num5 < list2.Count; num5++)
			{
				if (num4 <= list2[num5].Weight)
				{
					return new RuntimePerkData(list2[num5].Data, list2[num5].Rarity);
				}
				num4 -= list2[num5].Weight;
			}
			return new RuntimePerkData(list2[0].Data, list2[0].Rarity);
		}

		private Dictionary<PerkRarity, List<PerkData>> GetPerksPerRarityMap(List<PerkData> poolList, HashSet<PerkRarity> supportedRarities)
		{
			Dictionary<PerkRarity, List<PerkData>> dictionary = new Dictionary<PerkRarity, List<PerkData>>();
			PlayerHand.Instance.TryGetAllPerks(out var perks);
			foreach (PerkRarity supportedRarity in supportedRarities)
			{
				dictionary.Add(supportedRarity, new List<PerkData>());
				foreach (PerkData data in poolList)
				{
					if (!data.HasRarity(supportedRarity) || IsPerkMaxedOut(data))
					{
						continue;
					}
					RuntimePerk runtimePerk = perks?.FirstOrDefault((RuntimePerk rp) => rp.RuntimeData.Data.ID == data.ID);
					if (runtimePerk != null)
					{
						PerkRarity perkRarity = (CanPerkBeUpgraded(runtimePerk) ? (runtimePerk.CurrentRarity + 1) : runtimePerk.CurrentRarity);
						if (supportedRarity == perkRarity)
						{
							dictionary[supportedRarity].Add(data);
						}
					}
					else if (supportedRarity == data.GetLowestRarity())
					{
						dictionary[supportedRarity].Add(data);
					}
				}
			}
			return dictionary;
		}

		private bool IsPerkMaxedOut(PerkData data)
		{
			if (!PlayerHand.Instance.TryGetAllPerks(out var perks))
			{
				return false;
			}
			return perks.FirstOrDefault((RuntimePerk element) => element.RuntimeData.Data.ID == data.ID)?.IsMaxedOut ?? false;
		}

		private bool CanPerkBeUpgraded(RuntimePerk perk)
		{
			if (perk == null)
			{
				return false;
			}
			if (perk.ReachedRarityMaxLevel)
			{
				return !perk.ReachedMaxRarity;
			}
			return false;
		}

		private void FilterPerksBySupportedRarities(List<PerkData> poolList, HashSet<PerkRarity> supportedRarities)
		{
			poolList.RemoveAll((PerkData perk) => !perk || !perk.HasAnyRarity(supportedRarities));
		}

		private bool AreDependenciesMet(PerkData perkData, List<RuntimeEquipmentData> chosenEquipments)
		{
			if (perkData.Dependencies == null || perkData.Dependencies.Length == 0)
			{
				return true;
			}
			if (chosenEquipments == null)
			{
				return false;
			}
			CardData[] dependencies = perkData.Dependencies;
			foreach (CardData cardData in dependencies)
			{
				EquipmentData requiredEquipment = cardData as EquipmentData;
				if ((object)requiredEquipment != null && !chosenEquipments.Any((RuntimeEquipmentData runtimeEquipmentData) => runtimeEquipmentData.BaseData.ID == requiredEquipment.ID))
				{
					return false;
				}
			}
			return true;
		}

		private PerkDropTier GetBestFitTier(RuntimePerkData[] results)
		{
			if (results == null || results.Length == 0)
			{
				return PerkDropTier.Basic;
			}
			PerkDropTierWeights perkDropTierWeights = ((_currentLevelDrops.Drop.Length != 0) ? _currentLevelDrops.Drop[0] : default(PerkDropTierWeights));
			int num = -1;
			for (int i = 0; i < _currentLevelDrops.Drop.Length; i++)
			{
				PerkDropTierWeights perkDropTierWeights2 = _currentLevelDrops.Drop[i];
				int num2 = 0;
				for (int j = 0; j < results.Length; j++)
				{
					if (perkDropTierWeights2.SupportedRarities.Contains(results[j].Rarity))
					{
						num2++;
					}
				}
				if (num2 > num)
				{
					num = num2;
					perkDropTierWeights = perkDropTierWeights2;
				}
				else if (num2 == num && perkDropTierWeights2.Tier > perkDropTierWeights.Tier)
				{
					perkDropTierWeights = perkDropTierWeights2;
				}
			}
			return perkDropTierWeights.Tier;
		}

		public void UpdateWeights(int playerLevel)
		{
			int levelThresholdsCount = _perkDropWeightsData.LevelThresholdsCount;
			if (levelThresholdsCount != 0)
			{
				int num = 0;
				for (int i = 0; i < levelThresholdsCount && playerLevel >= PerkDropWeights.GetPerLevelThresholdDrop(i).Level; i++)
				{
					num = i;
				}
				_currentLevelThresholdIndex = num;
				int num2 = Mathf.Min(num + 1, levelThresholdsCount - 1);
				PerkDropPerLevelThreshold perLevelThresholdDrop = PerkDropWeights.GetPerLevelThresholdDrop(num);
				PerkDropPerLevelThreshold perLevelThresholdDrop2 = PerkDropWeights.GetPerLevelThresholdDrop(num2);
				if (num == num2 || playerLevel >= perLevelThresholdDrop2.Level || perLevelThresholdDrop.Level == perLevelThresholdDrop2.Level)
				{
					_currentLevelDrops = perLevelThresholdDrop;
					return;
				}
				float t = (float)(playerLevel - perLevelThresholdDrop.Level) / (float)(perLevelThresholdDrop2.Level - perLevelThresholdDrop.Level);
				_currentLevelDrops = InterpolateThresholds(perLevelThresholdDrop, perLevelThresholdDrop2, t, playerLevel);
			}
		}

		private PerkDropPerLevelThreshold InterpolateThresholds(PerkDropPerLevelThreshold baseDrop, PerkDropPerLevelThreshold nextDrop, float t, int currentLevel)
		{
			Dictionary<PerkDropTier, PerkDropTierWeights> dictionary = new Dictionary<PerkDropTier, PerkDropTierWeights>();
			if (baseDrop.Drop != null)
			{
				PerkDropTierWeights[] drop = baseDrop.Drop;
				for (int i = 0; i < drop.Length; i++)
				{
					PerkDropTierWeights value = drop[i];
					dictionary[value.Tier] = value;
				}
			}
			Dictionary<PerkDropTier, PerkDropTierWeights> dictionary2 = new Dictionary<PerkDropTier, PerkDropTierWeights>();
			if (nextDrop.Drop != null)
			{
				PerkDropTierWeights[] drop = nextDrop.Drop;
				for (int i = 0; i < drop.Length; i++)
				{
					PerkDropTierWeights value2 = drop[i];
					dictionary2[value2.Tier] = value2;
				}
			}
			HashSet<PerkDropTier> hashSet = new HashSet<PerkDropTier>(dictionary.Keys);
			hashSet.UnionWith(dictionary2.Keys);
			List<PerkDropTierWeights> list = new List<PerkDropTierWeights>();
			foreach (PerkDropTier item in hashSet)
			{
				PerkDropTierWeights value3;
				bool num = dictionary.TryGetValue(item, out value3);
				PerkDropTierWeights value4;
				bool flag = dictionary2.TryGetValue(item, out value4);
				float a = 0f;
				if (num)
				{
					a = value3.Weight;
				}
				float b = 0f;
				if (flag)
				{
					b = value4.Weight;
				}
				float weight = Mathf.Lerp(a, b, t);
				Dictionary<PerkRarity, PerkRarityWeight> dictionary3 = new Dictionary<PerkRarity, PerkRarityWeight>();
				if (num && value3.RarityWeights != null)
				{
					foreach (PerkRarityWeight rarityWeight in value3.RarityWeights)
					{
						dictionary3[rarityWeight.Rarity] = rarityWeight;
					}
				}
				Dictionary<PerkRarity, PerkRarityWeight> dictionary4 = new Dictionary<PerkRarity, PerkRarityWeight>();
				if (flag && value4.RarityWeights != null)
				{
					foreach (PerkRarityWeight rarityWeight2 in value4.RarityWeights)
					{
						dictionary4[rarityWeight2.Rarity] = rarityWeight2;
					}
				}
				HashSet<PerkRarity> hashSet2 = new HashSet<PerkRarity>(dictionary3.Keys);
				hashSet2.UnionWith(dictionary4.Keys);
				List<PerkRarityWeight> list2 = new List<PerkRarityWeight>();
				foreach (PerkRarity item2 in hashSet2)
				{
					PerkRarityWeight value5;
					bool num2 = dictionary3.TryGetValue(item2, out value5);
					PerkRarityWeight value6;
					bool flag2 = dictionary4.TryGetValue(item2, out value6);
					float a2 = 0f;
					if (num2)
					{
						a2 = value5.Weight;
					}
					float b2 = 0f;
					if (flag2)
					{
						b2 = value6.Weight;
					}
					float weight2 = Mathf.Lerp(a2, b2, t);
					list2.Add(new PerkRarityWeight(item2, weight2));
				}
				list.Add(new PerkDropTierWeights(item, weight, list2));
			}
			return new PerkDropPerLevelThreshold(currentLevel, list.ToArray());
		}

		public void BanPerk(RuntimePerkData runtimePerkData)
		{
			PerkData data = runtimePerkData.Data;
			if (!_bannedPerksData.ContainsKey(PerkPoolID.Beatrice))
			{
				_bannedPerksData.Add(PerkPoolID.Beatrice, new List<PerkData>());
			}
			_bannedPerksData[PerkPoolID.Beatrice].Add(data);
			_perkPools[PerkPoolID.Beatrice].Remove(data);
		}

		private void DebugLogCurrentWeights()
		{
			if (_currentLevelDrops.Drop == null || _currentLevelDrops.Drop.Length == 0)
			{
				Debug.LogWarning("PERK POOL DEBUG: No drops configured for current level.");
				return;
			}
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.AppendLine($"--- PERK POOL WEIGHTS (Calculated for Level: {_currentLevelDrops.Level}) ---");
			PerkDropTierWeights[] drop = _currentLevelDrops.Drop;
			for (int i = 0; i < drop.Length; i++)
			{
				PerkDropTierWeights perkDropTierWeights = drop[i];
				stringBuilder.AppendLine($"[Tier: {perkDropTierWeights.Tier}] - Base Weight: {perkDropTierWeights.Weight:F2}");
				if (perkDropTierWeights.RarityWeights != null && perkDropTierWeights.RarityWeights.Count > 0)
				{
					foreach (PerkRarityWeight rarityWeight in perkDropTierWeights.RarityWeights)
					{
						stringBuilder.AppendLine($"   -> {rarityWeight.Rarity}: {rarityWeight.Weight:F2}");
					}
				}
				else
				{
					stringBuilder.AppendLine("   -> No Rarity Weights configured.");
				}
			}
			stringBuilder.AppendLine("-----------------------------------");
			Debug.Log(stringBuilder.ToString());
		}
	}
}
