using System;
using System.Linq;
using AstralShift.HellMaiden.Combat.Hand;
using UnityEngine;

namespace AstralShift.HellMaiden.Data.Cards
{
	[Serializable]
	public class EquipmentLevelModifiersData
	{
		[SerializeField]
		protected EquipmentDataModifier[] modifiers;

		[SerializeField]
		private bool overrideDescription;

		[SerializeField]
		protected string descriptionKey;

		public EquipmentDataModifier[] Modifiers => modifiers;

		public bool OverrideDescription => overrideDescription;

		public ref string DescriptionKey => ref descriptionKey;

		public EquipmentDataModifier[] GetStaticStatModifiers()
		{
			return modifiers.Where((EquipmentDataModifier modifier) => DataModifierResolver.TryGetEquipmentBaseTypeByID(modifier.ModifierID, out var baseType) && baseType == typeof(StaticStatModifier)).ToArray();
		}

		public EquipmentDataModifier[] GetDynamicStatModifiers()
		{
			return modifiers.Where((EquipmentDataModifier modifier) => DataModifierResolver.TryGetEquipmentBaseTypeByID(modifier.ModifierID, out var baseType) && baseType == typeof(DynamicStatModifier)).ToArray();
		}

		public EquipmentDataModifier[] GetDynamicOnDamageModifiers()
		{
			return modifiers.Where((EquipmentDataModifier modifier) => DataModifierResolver.TryGetEquipmentBaseTypeByID(modifier.ModifierID, out var baseType) && baseType == typeof(DynamicOnDamageModifier)).ToArray();
		}

		public EquipmentDataModifier[] GetOnHitModifiers()
		{
			return modifiers.Where((EquipmentDataModifier modifier) => DataModifierResolver.TryGetEquipmentBaseTypeByID(modifier.ModifierID, out var baseType) && baseType == typeof(OnHitModifier)).ToArray();
		}

		public EquipmentDataModifier[] GetOnKillModifiers()
		{
			return modifiers.Where((EquipmentDataModifier modifier) => DataModifierResolver.TryGetEquipmentBaseTypeByID(modifier.ModifierID, out var baseType) && baseType == typeof(OnKillModifier)).ToArray();
		}
	}
}
