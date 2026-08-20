using System;
using UnityEngine;

namespace AstralShift.HellMaiden.Data
{
	[Serializable]
	public class EquipmentModifierID : IEquatable<EquipmentModifierID>
	{
		[SerializeField]
		protected uint value;

		public uint Value => value;

		public EquipmentModifierID(uint value)
		{
			this.value = value;
		}

		public bool Equals(EquipmentModifierID other)
		{
			return value == other.value;
		}

		public override bool Equals(object obj)
		{
			if (obj is EquipmentModifierID other)
			{
				return Equals(other);
			}
			return false;
		}

		public override int GetHashCode()
		{
			return (int)value;
		}

		public override string ToString()
		{
			return $"ModifierId({value})";
		}

		public static implicit operator uint(EquipmentModifierID id)
		{
			return id.value;
		}

		public static explicit operator EquipmentModifierID(uint v)
		{
			return new EquipmentModifierID(v);
		}
	}
}
