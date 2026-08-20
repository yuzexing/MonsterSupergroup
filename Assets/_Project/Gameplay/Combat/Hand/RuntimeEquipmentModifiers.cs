using System;
using System.Collections.Generic;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[Serializable]
	public class RuntimeEquipmentModifiers
	{
		public List<StaticStatModifier> StaticModifiers = new List<StaticStatModifier>();

		public List<DynamicStatModifier> DynamicModifiers = new List<DynamicStatModifier>();

		public List<DynamicOnDamageModifier> DynamicOnDamageModifiers = new List<DynamicOnDamageModifier>();

		public List<OnHitModifier> OnHitModifiers = new List<OnHitModifier>();

		public List<OnKillModifier> OnKillModifiers = new List<OnKillModifier>();

		public List<RuntimeEquipmentModifier> MultiSlotModifiers = new List<RuntimeEquipmentModifier>();

		public bool HasModifiers
		{
			get
			{
				if (StaticModifiers.Count <= 0 && DynamicModifiers.Count <= 0 && DynamicOnDamageModifiers.Count <= 0 && OnHitModifiers.Count <= 0 && OnKillModifiers.Count <= 0)
				{
					return MultiSlotModifiers.Count > 0;
				}
				return true;
			}
		}

		public void Add(RuntimeEquipmentModifier modifier)
		{
		}

		public void Remove(RuntimeEquipmentModifier modifier)
		{
		}

		public void Clear()
		{
			StaticModifiers?.Clear();
			DynamicModifiers?.Clear();
			DynamicOnDamageModifiers?.Clear();
			OnHitModifiers?.Clear();
			OnKillModifiers?.Clear();
			MultiSlotModifiers?.Clear();
		}
	}
}
