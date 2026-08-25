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
            : this(
                CombatContext.None,
                target,
                weapon,
                damageInfo,
                damageInfo,
                random,
                onHitChanceMultiplier,
                burnDamageMultiplier)
        {
        }

        public OnHitModifierArgs(
            CombatContext context,
            ICombatTarget target,
            IWeaponRuntime weapon,
            DamageInfo resolvedDamageInfo,
            DamageInfo predictedAppliedDamageInfo,
            IRandomSource random,
            float onHitChanceMultiplier,
            float burnDamageMultiplier)
        {
            Context = context;
            Target = target ?? throw new ArgumentNullException(nameof(target));
            Weapon = weapon ?? throw new ArgumentNullException(nameof(weapon));
            Random = random ?? throw new ArgumentNullException(nameof(random));
            ResolvedDamageInfo = resolvedDamageInfo;
            DamageInfo = predictedAppliedDamageInfo;
            OnHitChanceMultiplier = onHitChanceMultiplier;
            BurnDamageMultiplier = burnDamageMultiplier;
        }

        public CombatContext Context { get; }

        public ICombatTarget Target { get; }

        public IWeaponRuntime Weapon { get; }

        public DamageInfo DamageInfo { get; }

        /// <summary>
        /// Raw damage resolved by the owner client before the predicted target clamps it.
        /// This is the value submitted to the canonical server ledger.
        /// </summary>
        public DamageInfo ResolvedDamageInfo { get; }

        public IRandomSource Random { get; }

        public float OnHitChanceMultiplier { get; }

        public float BurnDamageMultiplier { get; }
    }

    public readonly struct OnPredictedLethalHitModifierArgs
    {
        public OnPredictedLethalHitModifierArgs(
            CombatContext context,
            ICombatTarget target,
            IWeaponRuntime weapon,
            DamageInfo resolvedDamageInfo,
            DamageInfo predictedAppliedDamageInfo,
            IRandomSource random,
            float chanceMultiplier)
        {
            Context = context;
            Target = target ?? throw new ArgumentNullException(nameof(target));
            Weapon = weapon ?? throw new ArgumentNullException(nameof(weapon));
            Random = random ?? throw new ArgumentNullException(nameof(random));
            ResolvedDamageInfo = resolvedDamageInfo;
            PredictedAppliedDamageInfo = predictedAppliedDamageInfo;
            ChanceMultiplier = chanceMultiplier;
        }

        public CombatContext Context { get; }
        public ICombatTarget Target { get; }
        public IWeaponRuntime Weapon { get; }
        public DamageInfo ResolvedDamageInfo { get; }
        public DamageInfo PredictedAppliedDamageInfo { get; }
        public IRandomSource Random { get; }
        public float ChanceMultiplier { get; }
    }

    /// <summary>
    /// Compatibility view for migrated HellMaiden modifiers. This stage now means
    /// owner-predicted lethal hit and must not grant canonical rewards.
    /// </summary>
    public readonly struct OnKillModifierArgs
    {
        public OnKillModifierArgs(
            ICombatTarget target,
            IWeaponRuntime weapon,
            DamageInfo damageInfo,
            IRandomSource random,
            float onKillChanceMultiplier)
            : this(new OnPredictedLethalHitModifierArgs(
                CombatContext.None,
                target,
                weapon,
                damageInfo,
                damageInfo,
                random,
                onKillChanceMultiplier))
        {
        }

        public OnKillModifierArgs(OnPredictedLethalHitModifierArgs args)
        {
            Context = args.Context;
            Target = args.Target;
            Weapon = args.Weapon;
            Random = args.Random;
            ResolvedDamageInfo = args.ResolvedDamageInfo;
            DamageInfo = args.PredictedAppliedDamageInfo;
            OnKillChanceMultiplier = args.ChanceMultiplier;
        }

        public CombatContext Context { get; }

        public ICombatTarget Target { get; }

        public IWeaponRuntime Weapon { get; }

        public DamageInfo DamageInfo { get; }

        public DamageInfo ResolvedDamageInfo { get; }

        public IRandomSource Random { get; }

        public float OnKillChanceMultiplier { get; }

        internal OnPredictedLethalHitModifierArgs AsPredictedLethalHitArgs()
        {
            return new OnPredictedLethalHitModifierArgs(
                Context,
                Target,
                Weapon,
                ResolvedDamageInfo,
                DamageInfo,
                Random,
                OnKillChanceMultiplier);
        }
    }
}
