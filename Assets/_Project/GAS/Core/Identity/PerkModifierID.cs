using System;

namespace MonsterSupergroup.GAS
{
    [Serializable]
    public readonly struct PerkModifierID : IEquatable<PerkModifierID>
    {
        private readonly uint _value;

        public PerkModifierID(uint value)
        {
            _value = value;
        }

        public static PerkModifierID Invalid => default;

        public uint Value => _value;

        public bool IsValid => _value != 0u;

        public bool Equals(PerkModifierID other)
        {
            return _value == other._value;
        }

        public override bool Equals(object obj)
        {
            return obj is PerkModifierID other && Equals(other);
        }

        public override int GetHashCode()
        {
            return _value.GetHashCode();
        }

        public override string ToString()
        {
            return $"PerkModifierID({_value})";
        }

        public static bool operator ==(PerkModifierID left, PerkModifierID right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(PerkModifierID left, PerkModifierID right)
        {
            return !left.Equals(right);
        }

        public static implicit operator uint(PerkModifierID id)
        {
            return id._value;
        }

        public static explicit operator PerkModifierID(uint value)
        {
            return new PerkModifierID(value);
        }
    }
}
