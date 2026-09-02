using System;
using System.Linq;
using UnityEngine;

namespace AstralShift.HellMaiden.Data.Cards
{
	[Serializable]
	public class EquipmentLevelModifiersData
	{
		[SerializeField]
		private EquipmentModifierApplication[] nativeModifiers;

		[SerializeField]
		private bool overrideDescription;

		[SerializeField]
		protected string descriptionKey;

		public EquipmentModifierApplication[] Modifiers =>
			nativeModifiers ?? Array.Empty<EquipmentModifierApplication>();

		public bool OverrideDescription => overrideDescription;

		public ref string DescriptionKey => ref descriptionKey;

		public void ConfigureNative(EquipmentModifierApplication[] applications)
		{
			nativeModifiers = applications ??
				Array.Empty<EquipmentModifierApplication>();
		}

		public EquipmentModifierApplication[] GetStaticStatModifiers()
		{
			return Modifiers.Where(modifier =>
				GetFamily(modifier.ModifierIdValue) == 0x01u).ToArray();
		}

		public EquipmentModifierApplication[] GetDynamicStatModifiers()
		{
			return Modifiers.Where(modifier =>
				GetFamily(modifier.ModifierIdValue) == 0x04u).ToArray();
		}

		public EquipmentModifierApplication[] GetDynamicOnDamageModifiers()
		{
			return Modifiers.Where(modifier =>
				GetFamily(modifier.ModifierIdValue) == 0x05u).ToArray();
		}

		public EquipmentModifierApplication[] GetOnHitModifiers()
		{
			return Modifiers.Where(modifier =>
				GetFamily(modifier.ModifierIdValue) == 0x02u).ToArray();
		}

		public EquipmentModifierApplication[] GetOnKillModifiers()
		{
			return Modifiers.Where(modifier =>
				GetFamily(modifier.ModifierIdValue) == 0x03u).ToArray();
		}

		private static uint GetFamily(uint modifierId) =>
			(modifierId >> 24) & 0xFFu;
	}
}
