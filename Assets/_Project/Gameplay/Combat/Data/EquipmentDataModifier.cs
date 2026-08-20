using System;
using UnityEngine;

namespace AstralShift.HellMaiden.Data
{
	[Serializable]
	public class EquipmentDataModifier
	{
		[SerializeReference]
		private EquipmentModifierID modifierID = new EquipmentModifierID(0u);

		[SerializeReference]
		private object parameters;

		[SerializeField]
		private bool multiSlotConfig;

		[SerializeField]
		private EquipmentMultiSlotConfig multiSlot;

		public EquipmentModifierID ModifierID => modifierID;

		public string DisplayName
		{
			get
			{
				DataModifierResolver.TryGetEquipmentDisplayName(modifierID, out var display);
				return display;
			}
		}

		public string Name
		{
			get
			{
				DataModifierResolver.TryGetEquipmentDisplayName(modifierID, out var display);
				return display.Replace(" ", "");
			}
		}

		public object Parameters => parameters;

		public bool HasMultiSlotConfig
		{
			get
			{
				return multiSlotConfig;
			}
			set
			{
				multiSlotConfig = value;
			}
		}

		public EquipmentMultiSlotConfig MultiSlot => multiSlot;

		public float GetParameterByIndex(int idx)
		{
			object modifierParamByIndex = DataModifierUtils.GetModifierParamByIndex(parameters, idx);
			if (!(modifierParamByIndex is int num))
			{
				if (!(modifierParamByIndex is float result))
				{
					if (modifierParamByIndex is double num2)
					{
						return (float)num2;
					}
					throw new InvalidCastException($"Parameter at index {idx} is not numeric");
				}
				return result;
			}
			return num;
		}
	}
}
