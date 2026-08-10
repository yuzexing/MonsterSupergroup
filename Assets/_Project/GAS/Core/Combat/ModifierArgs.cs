using System;

namespace MonsterSupergroup.GAS
{
    public readonly struct OnHitModifierArgs
    {
        public OnHitModifierArgs(
            ICombatTarget target,
            IWeaponRuntime weapon,
            DamageInfo damageInfo,
            IRandomSource random,
            float onHitChanceMultiplier,
            float burnDamageMultiplier)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            Weapon = weapon ?? throw new ArgumentNullException(nameof(weapon));
            Random = random ?? throw new ArgumentNullException(nameof(random));
            DamageInfo = damageInfo;
            OnHitChanceMultiplier = onHitChanceMultiplier;
            BurnDamageMultiplier = burnDamageMultiplier;
        }

        public ICombatTarget Target { get; }

        public IWeaponRuntime Weapon { get; }

        public DamageInfo DamageInfo { get; }

        public IRandomSource Random { get; }

        public float OnHitChanceMultiplier { get; }

        public float BurnDamageMultiplier { get; }
    }

    public readonly struct OnKillModifierArgs
    {
        public OnKillModifierArgs(
            ICombatTarget target,
            IWeaponRuntime weapon,
            DamageInfo damageInfo,
            IRandomSource random,
            float onKillChanceMultiplier)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            Weapon = weapon ?? throw new ArgumentNullException(nameof(weapon));
            Random = random ?? throw new ArgumentNullException(nameof(random));
            DamageInfo = damageInfo;
            OnKillChanceMultiplier = onKillChanceMultiplier;
        }

        public ICombatTarget Target { get; }

        public IWeaponRuntime Weapon { get; }

        public DamageInfo DamageInfo { get; }

        public IRandomSource Random { get; }

        public float OnKillChanceMultiplier { get; }
    }
}
