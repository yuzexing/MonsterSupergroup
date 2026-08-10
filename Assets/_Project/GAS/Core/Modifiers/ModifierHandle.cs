using System;

namespace MonsterSupergroup.GAS
{
    public readonly struct ModifierHandle : IEquatable<ModifierHandle>
    {
        internal ModifierHandle(long value)
        {
            Value = value;
        }

        public long Value { get; }

        public bool IsValid => Value != 0;

        public bool Equals(ModifierHandle other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is ModifierHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public static bool operator ==(ModifierHandle left, ModifierHandle right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ModifierHandle left, ModifierHandle right)
        {
            return !left.Equals(right);
        }
    }
}
