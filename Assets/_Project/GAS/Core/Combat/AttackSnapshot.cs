using System;

namespace MonsterSupergroup.GAS
{
    public sealed class AttackSnapshot
    {
        internal AttackSnapshot(
            IWeaponRuntime weapon,
            AttackStatsSnapshot stats,
            CombatContext context)
        {
            Weapon = weapon ?? throw new ArgumentNullException(nameof(weapon));
            Stats = stats;
            Context = context.IsValid
                ? context
                : throw new ArgumentException("Attack context must be valid.", nameof(context));
        }

        public IWeaponRuntime Weapon { get; }

        public AttackStatsSnapshot Stats { get; }

        public CombatContext Context { get; }
    }
}
