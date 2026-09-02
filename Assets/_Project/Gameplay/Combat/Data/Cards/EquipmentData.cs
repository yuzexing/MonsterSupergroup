using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace AstralShift.HellMaiden.Data.Cards
{
	[CreateAssetMenu(fileName = "New Equipment Data", menuName = "HellMaiden/Data/Cards/Equipment Data")]
	public class EquipmentData : CardData
	{
		[Header("Equipment Settings")]
		public EquipmentCardType cardType;

		public ModifierFlags usedStatsModifiers;

		[SerializeField]
		protected EquipmentLevelModifiersData[] levelModifiersData;

		private static string _descriptionRegexPattern;

		public EquipmentLevelModifiersData[] Levels => levelModifiersData;

		public override string GetDescription()
		{
			return GetDescription(0u);
		}

		public string GetDescription(uint levelIndex)
		{
			string term = (Levels[levelIndex].OverrideDescription ? Levels[levelIndex].DescriptionKey : Description);
			if (hasLocalization)
			{
				term = (Levels[levelIndex].OverrideDescription ? Levels[levelIndex].DescriptionKey : DescriptionKey);
				LocalizationMediator.GetTranslation(ref term);
			}
			Dictionary<string, EquipmentModifierApplication> foundModifiersMap =
				new Dictionary<string, EquipmentModifierApplication>();
			EquipmentModifierApplication[] modifiers =
				levelModifiersData[levelIndex].Modifiers;
			foreach (EquipmentModifierApplication equipmentDataModifier in modifiers)
			{
				foundModifiersMap.TryAdd(
					equipmentDataModifier.DescriptionToken,
					equipmentDataModifier);
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
					if (!foundModifiersMap.TryGetValue(match.Groups[1].Value, out var value))
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
	}
}
