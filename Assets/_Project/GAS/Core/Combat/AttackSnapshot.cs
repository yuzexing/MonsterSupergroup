using System;

namespace MonsterSupergroup.GAS
{
    public sealed class AttackSnapshot
    {
        internal AttackSnapshot(IWeaponRuntime weapon, AttackStatsSnapshot stats)
        {
            Weapon = weapon ?? throw new ArgumentNullException(nameof(weapon));
            Stats = stats;
        }

        public IWeaponRuntime Weapon { get; }

        public AttackStatsSnapshot Stats { get; }
    }
}
