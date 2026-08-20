using System;
using System.Linq;
using AstralShift.HellMaiden.Combat.Hand;
using UnityEngine;

namespace AstralShift.HellMaiden.Data.Perks
{
	[Serializable]
	public class PerkRarityModifiersData
	{
		[SerializeField]
		protected PerkRarity rarity;

		[SerializeField]
		protected PerkDataModifier[] modifiers;

		public PerkRarity Rarity => rarity;

		public PerkDataModifier[] Modifiers => modifiers;

		public PerkDataModifier[] GetPlayerModifiers()
		{
			return modifiers.Where((PerkDataModifier modifier) => DataModifierResolver.TryGetPerkBaseTypeByID(modifier.ModifierID, out var baseType) && baseType == typeof(PlayerPerkModifier)).ToArray();
		}

		public PerkDataModifier[] GetWeaponModifiers()
		{
			return modifiers.Where((PerkDataModifier modifier) => DataModifierResolver.TryGetPerkBaseTypeByID(modifier.ModifierID, out var baseType) && baseType == typeof(WeaponStatsPerkModifier)).ToArray();
		}

		public PerkDataModifier[] GetPlayerConditionModifiers()
		{
			return modifiers.Where((PerkDataModifier modifier) => DataModifierResolver.TryGetPerkBaseTypeByID(modifier.ModifierID, out var baseType) && baseType == typeof(PlayerConditionPerkModifier)).ToArray();
		}

		public PerkDataModifier[] GetEnemyConditionModifiers()
		{
			return modifiers.Where((PerkDataModifier modifier) => DataModifierResolver.TryGetPerkBaseTypeByID(modifier.ModifierID, out var baseType) && baseType == typeof(EnemyConditionPerkModifier)).ToArray();
		}
	}
}
