using System;

namespace MonsterSupergroup.GAS
{
    public readonly struct DamageInfo : IEquatable<DamageInfo>
    {
        public DamageInfo(uint id, int value, bool isCritical)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Damage cannot be negative.");
            }

            Id = id;
            Value = value;
            IsCritical = isCritical;
        }

        public uint Id { get; }

        public int Value { get; }

        public bool IsCritical { get; }

        public bool Equals(DamageInfo other)
        {
            return Id == other.Id && Value == other.Value && IsCritical == other.IsCritical;
        }

        public override bool Equals(object obj)
        {
            return obj is DamageInfo other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Id * 397) ^ (Value * 31) ^ (IsCritical ? 1 : 0);
            }
        }
    }
}
