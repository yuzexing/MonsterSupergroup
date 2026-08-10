using System;

namespace MonsterSupergroup.GAS
{
    [Serializable]
    public readonly struct EquipmentModifierID : IEquatable<EquipmentModifierID>
    {
        private readonly uint _value;

        public EquipmentModifierID(uint value)
        {
            _value = value;
        }

        public static EquipmentModifierID Invalid => default;

        public uint Value => _value;

        public bool IsValid => _value != 0u;

        public bool Equals(EquipmentModifierID other)
        {
            return _value == other._value;
        }

        public override bool Equals(object obj)
        {
            return obj is EquipmentModifierID other && Equals(other);
        }

        public override int GetHashCode()
        {
            return _value.GetHashCode();
        }

        public override string ToString()
        {
            return $"EquipmentModifierID({_value})";
        }

        public static bool operator ==(EquipmentModifierID left, EquipmentModifierID right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(EquipmentModifierID left, EquipmentModifierID right)
        {
            return !left.Equals(right);
        }

        public static implicit operator uint(EquipmentModifierID id)
        {
            return id._value;
        }

        public static explicit operator EquipmentModifierID(uint value)
        {
            return new EquipmentModifierID(value);
        }
    }
}
