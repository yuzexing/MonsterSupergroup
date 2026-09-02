using System;
using UnityEngine;

namespace AstralShift.HellMaiden.Data.Perks
{
	[Serializable]
	public class PerkRarityModifiersData
	{
		[SerializeField]
		protected PerkRarity rarity;

		[SerializeField]
		protected PerkModifierApplication[] modifiers;

		public PerkRarity Rarity => rarity;

		public PerkModifierApplication[] Modifiers =>
			modifiers ?? Array.Empty<PerkModifierApplication>();

		public PerkModifierApplication[] GetPlayerModifiers()
		{
			return Array.FindAll(Modifiers, modifier =>
				modifier.Domain == PerkApplicationDomain.PlayerAttributes);
		}

		public PerkModifierApplication[] GetWeaponModifiers()
		{
			return Array.FindAll(Modifiers, modifier =>
				modifier.Domain == PerkApplicationDomain.WeaponStats);
		}

		public PerkModifierApplication[] GetPlayerConditionModifiers()
		{
			return Array.FindAll(Modifiers, modifier =>
				modifier.Domain == PerkApplicationDomain.ConditionalCombat);
		}

		public PerkModifierApplication[] GetEnemyConditionModifiers()
		{
			return Array.FindAll(Modifiers, modifier =>
				modifier.Domain == PerkApplicationDomain.ConditionalCombat);
		}

		public void Configure(
			PerkRarity newRarity,
			PerkModifierApplication[] newModifiers)
		{
			rarity = newRarity;
			modifiers = newModifiers ?? Array.Empty<PerkModifierApplication>();
		}
	}
}
