using System;
using UnityEngine;

namespace AstralShift.HellMaiden.Data
{
	[Serializable]
	public class PerkDataModifier
	{
		[SerializeReference]
		private PerkModifierID modifierID = new PerkModifierID(0u);

		[SerializeReference]
		private object parameters;

		public PerkModifierID ModifierID => modifierID;

		public string DisplayName
		{
			get
			{
				DataModifierResolver.TryGetPerkDisplayName(modifierID, out var display);
				return display;
			}
		}

		public string Name
		{
			get
			{
				DataModifierResolver.TryGetPerkDisplayName(modifierID, out var display);
				return display.Replace(" ", "");
			}
		}

		public object Parameters => parameters;

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
