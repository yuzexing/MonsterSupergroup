using System;
using System.Collections.Generic;

namespace MonsterSupergroup.GAS
{
    public abstract class RuntimePerkModifier
    {
        protected RuntimePerkModifier(PerkModifierID id, PerkModifierParameters parameters)
        {
            ID = id;
            Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        }

        public PerkModifierID ID { get; }

        public PerkModifierParameters Parameters { get; }

        public bool TryStack(RuntimePerkModifier other)
        {
            if (other == null ||
                !EqualityComparer<PerkModifierID>.Default.Equals(ID, other.ID) ||
                Parameters.GetType() != other.Parameters.GetType())
            {
                return false;
            }

            return StackSameType(other);
        }

        protected abstract bool StackSameType(RuntimePerkModifier other);
    }

    public abstract class WeaponStatsPerkModifier : RuntimePerkModifier
    {
        protected WeaponStatsPerkModifier(PerkModifierID id, PerkModifierParameters parameters)
            : base(id, parameters)
        {
        }

        public abstract void Apply(AttackStatsMultipliers multipliers);
    }
}
