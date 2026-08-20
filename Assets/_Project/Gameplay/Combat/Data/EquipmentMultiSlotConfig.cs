using System;

namespace AstralShift.HellMaiden.Data
{
	[Serializable]
	public class EquipmentMultiSlotConfig
	{
		public bool isSelfApplied;

		public EquipmentModifierSlots leftSlots;

		public EquipmentModifierSlots rightSlots;

		public bool IsSelfApplied => isSelfApplied;

		public EquipmentModifierSlots LeftSlots => leftSlots;

		public EquipmentModifierSlots RightSlots => rightSlots;
	}
}
