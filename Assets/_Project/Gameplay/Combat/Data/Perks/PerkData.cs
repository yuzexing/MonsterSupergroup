using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AstralShift.HellMaiden.Data.Cards;
using UnityEngine;

namespace AstralShift.HellMaiden.Data.Perks
{
	[CreateAssetMenu(fileName = "New Perk Data", menuName = "HellMaiden/Data/Perk Data")]
	public class PerkData : ScriptableObject
	{
		[Header("General Settings")]
		public uint ID;

		public string Title;

		[TextArea]
		[SerializeField]
		protected string Description;

		public bool hasLocalization;

		[SerializeField]
		protected string TitleKey;

		[SerializeField]
		protected string DescriptionKey;

		[SerializeField]
		private Sprite icon;

		public float poolWeight = 1f;

		public CardData[] Dependencies;

		[SerializeField]
		protected PerkRarityModifiersData[] perRarityModifiers;

		public static readonly int LevelsPerRarity = 3;

		private static string _descriptionRegexPattern;

		private HashSet<PerkRarity> _cachedRarities;

		public int RaritiesCount => perRarityModifiers?.Length ?? 0;

		public PerkRarityModifiersData[] GetAllRarities()
		{
			return perRarityModifiers ?? Array.Empty<PerkRarityModifiersData>();
		}

		public PerkRarityModifiersData GetRarity(PerkRarity rarity)
		{
			return GetAllRarities().First(
				(PerkRarityModifiersData element) =>
					element != null && element.Rarity == rarity);
		}

		public void ConfigureNativeModifiers(
			PerkRarityModifiersData[] rarityModifiers)
		{
			perRarityModifiers = rarityModifiers ??
				throw new ArgumentNullException(nameof(rarityModifiers));
			_cachedRarities = null;
			ValidateNativeGas();
		}

		public void ValidateNativeGas()
		{
			if (ID == 0u)
			{
				throw new InvalidOperationException(
					$"Perk '{name}' has a zero content ID.");
			}

			var foundRarities = new HashSet<PerkRarity>();
			PerkRarityModifiersData[] rarities = GetAllRarities();
			if (rarities.Length == 0)
			{
				throw new InvalidOperationException(
					$"Perk '{name}' has no rarity definitions.");
			}

			for (int rarityIndex = 0; rarityIndex < rarities.Length; rarityIndex++)
			{
				PerkRarityModifiersData rarity = rarities[rarityIndex]
					?? throw new InvalidOperationException(
						$"Perk '{name}' has a null rarity at index {rarityIndex}.");
				if (!foundRarities.Add(rarity.Rarity))
				{
					throw new InvalidOperationException(
						$"Perk '{name}' defines {rarity.Rarity} more than once.");
				}

				PerkModifierApplication[] modifiers = rarity.Modifiers;
				if (modifiers.Length == 0)
				{
					throw new InvalidOperationException(
						$"Perk '{name}' has no modifiers for {rarity.Rarity}.");
				}

				for (int modifierIndex = 0;
					modifierIndex < modifiers.Length;
					modifierIndex++)
				{
					PerkModifierApplication modifier = modifiers[modifierIndex]
						?? throw new InvalidOperationException(
							$"Perk '{name}' has a null modifier in " +
							$"{rarity.Rarity} at index {modifierIndex}.");
					if (modifier.Modifier == null ||
						!modifier.ModifierId.IsValid ||
						modifier.Parameters == null)
					{
						throw new InvalidOperationException(
							$"Perk '{name}' has an incomplete native modifier in " +
							$"{rarity.Rarity} at index {modifierIndex}.");
					}
				}
			}
		}

		public string GetTitle()
		{
			if (hasLocalization)
			{
				string term = TitleKey;
				LocalizationMediator.GetTranslation(ref term);
				return term;
			}
			return Title;
		}

		public string GetDescription(PerkRarity rarity)
		{
			string term = DescriptionKey;
			if (hasLocalization)
			{
				LocalizationMediator.GetTranslation(ref term);
			}
			else
			{
				term = Description;
			}
			Dictionary<string, PerkModifierApplication> modifiersMap =
				new Dictionary<string, PerkModifierApplication>();
			PerkModifierApplication[] modifiers = GetRarity(rarity).Modifiers;
			foreach (PerkModifierApplication perkDataModifier in modifiers)
			{
				modifiersMap[perkDataModifier.DescriptionToken] = perkDataModifier;
			}
			if (string.IsNullOrEmpty(_descriptionRegexPattern))
			{
				_descriptionRegexPattern =
					"\\{([^}]+)\\}\\[(\\d+)\\](?:\\[([^\\]]*)\\])?";
			}
			try
			{
				return Regex.Replace(term, _descriptionRegexPattern, delegate(Match match)
				{
					if (!modifiersMap.TryGetValue(match.Groups[1].Value, out var value))
					{
						return match.Value;
					}
					int idx = int.Parse(match.Groups[2].Value);
					float parameterByIndex = value.GetParameterByIndex(idx);
					return ((match.Groups[3].Success ? match.Groups[3].Value : string.Empty) == "%") ? (DataModifierUtils.FormatMultiplierToPercentage(parameterByIndex) + "%") : $"{parameterByIndex}";
				});
			}
			catch (Exception ex)
			{
				Debug.Log("Caught during regex: " + ex);
			}
			return null;
		}

		public Sprite GetIcon()
		{
			return icon;
		}

		private void InitializeRaritiesHashSet()
		{
			_cachedRarities = new HashSet<PerkRarity>();
			if (perRarityModifiers != null)
			{
				PerkRarityModifiersData[] array = perRarityModifiers;
				foreach (PerkRarityModifiersData perkRarityModifiersData in array)
				{
					_cachedRarities.Add(perkRarityModifiersData.Rarity);
				}
			}
		}

		public bool HasAnyRarity(HashSet<PerkRarity> rarities)
		{
			if (perRarityModifiers == null)
			{
				return false;
			}
			if (_cachedRarities == null)
			{
				InitializeRaritiesHashSet();
			}
			return _cachedRarities.Overlaps(rarities);
		}

		public PerkRarity GetLowestRarity()
		{
			if (perRarityModifiers == null)
			{
				return PerkRarity.Bronze;
			}
			if (_cachedRarities == null)
			{
				InitializeRaritiesHashSet();
			}
			return _cachedRarities.Min();
		}

		public PerkRarity GetHighestRarity()
		{
			if (perRarityModifiers == null)
			{
				return PerkRarity.Bronze;
			}
			if (_cachedRarities == null)
			{
				InitializeRaritiesHashSet();
			}
			return _cachedRarities.Max();
		}

		public bool HasRarity(PerkRarity rarity)
		{
			if (perRarityModifiers == null)
			{
				return false;
			}
			if (_cachedRarities == null)
			{
				InitializeRaritiesHashSet();
			}
			return _cachedRarities.Contains(rarity);
		}

		public bool HasRarity(uint rarity)
		{
			if (perRarityModifiers == null)
			{
				return false;
			}
			if (_cachedRarities == null)
			{
				InitializeRaritiesHashSet();
			}
			return _cachedRarities.Contains((PerkRarity)rarity);
		}

		public int GetMaxLevel()
		{
			PerkRarity lowestRarity = GetLowestRarity();
			PerkRarity highestRarity = GetHighestRarity();
			if (lowestRarity == highestRarity && lowestRarity == PerkRarity.Crystal)
			{
				return 1;
			}
			return (highestRarity - lowestRarity + 1) * LevelsPerRarity;
		}
	}
}
