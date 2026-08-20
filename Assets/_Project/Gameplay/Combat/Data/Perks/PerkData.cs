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

		public int RaritiesCount => perRarityModifiers.Length;

		public PerkRarityModifiersData[] GetAllRarities()
		{
			return perRarityModifiers;
		}

		public PerkRarityModifiersData GetRarity(PerkRarity rarity)
		{
			return perRarityModifiers.First((PerkRarityModifiersData element) => element.Rarity == rarity);
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
			Dictionary<string, PerkDataModifier> modifiersMap = new Dictionary<string, PerkDataModifier>();
			PerkDataModifier[] modifiers = GetRarity(rarity).Modifiers;
			foreach (PerkDataModifier perkDataModifier in modifiers)
			{
				modifiersMap.Add(perkDataModifier.Name, perkDataModifier);
			}
			if (string.IsNullOrEmpty(_descriptionRegexPattern))
			{
				string[] perkModifierNames = DataModifierResolver.PerkModifierNames;
				_descriptionRegexPattern = "\\{\\b(" + string.Join("|", perkModifierNames) + ")\\b\\}\\[(\\d+)\\](?:\\[([^\\]]*)\\])?";
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
