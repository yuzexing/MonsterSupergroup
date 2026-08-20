using System;
using UnityEngine;

namespace AstralShift.HellMaiden.Data
{
	[Serializable]
	public class PerkModifierID : IEquatable<PerkModifierID>
	{
		[SerializeField]
		protected uint value;

		public uint Value => value;

		public PerkModifierID(uint value)
		{
			this.value = value;
		}

		public bool Equals(PerkModifierID other)
		{
			return value == other.value;
		}

		public override bool Equals(object obj)
		{
			if (obj is PerkModifierID other)
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

		public static implicit operator uint(PerkModifierID id)
		{
			return id.value;
		}

		public static explicit operator PerkModifierID(uint v)
		{
			return new PerkModifierID(v);
		}
	}
}
