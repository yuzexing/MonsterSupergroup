using System;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.GAS;
using MonsterSupergroup.GAS.Unity;

namespace AstralShift.HellMaiden.Player.Attacks
{
    /// <summary>
    /// Compatibility boundary for HellMaiden-authored damage. It does not calculate
    /// damage or duplicate GAS modifiers; it submits the already calculated value
    /// through the existing CombatPipeline.
    /// </summary>
    public sealed class LegacyCombatExecution
    {
        private readonly CombatPipeline pipeline;

        public LegacyCombatExecution(CombatRuntimeServices services)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
            pipeline = new CombatPipeline(
                new MonsterSupergroup.GAS.RuntimeEquipmentModifiers(),
                new UnityRandomSource(),
                services.EventIds,
                services.EventSink,
                services.TriggerGuard,
                services.TimeSource);
        }

        public CombatRuntimeServices Services { get; }

        public CombatContext BeginAttack(uint abilityId, CombatTags tags)
        {
            return pipeline.BeginExternalAttack(
                Services.SourcePlayerId,
                Services.SourceEntityId,
                abilityId,
                tags | CombatTags.Attack);
        }

        public CombatResolution Resolve(
            LegacyDamageSource source,
            ICombatTarget target,
            DamageInfo damage)
        {
            if (!ReferenceEquals(source.Execution, this))
            {
                throw new InvalidOperationException(
                    "Legacy damage source belongs to another combat execution.");
            }

            CombatContext context = source.Context.WithTags(source.Tags);
            var gasDamage = new MonsterSupergroup.GAS.DamageInfo(
                source.DamageSourceId != 0u ? source.DamageSourceId : damage.id,
                Math.Max(0, damage.value),
                damage.isCritical);
            return pipeline.ResolvePrecomputedHit(context, target, gasDamage);
        }
    }

    public readonly struct LegacyDamageSource
    {
        public LegacyDamageSource(
            LegacyCombatExecution execution,
            CombatContext context,
            uint damageSourceId,
            CombatTags tags = CombatTags.None)
        {
            Execution = execution;
            Context = context;
            DamageSourceId = damageSourceId;
            Tags = tags;
        }

        public LegacyCombatExecution Execution { get; }
        public CombatContext Context { get; }
        public uint DamageSourceId { get; }
        public CombatTags Tags { get; }

        public bool IsValid => Execution != null && Context.IsValid;

        public bool ServicesArePlayerAuthored()
        {
            return IsValid && Execution.Services.SourcePlayerId != 0u &&
                Execution.Services.SourcePlayerId != uint.MaxValue;
        }

        public LegacyDamageSource WithContext(CombatContext context)
        {
            return new LegacyDamageSource(Execution, context, DamageSourceId, Tags);
        }

        public LegacyDamageSource WithTags(CombatTags tags)
        {
            return new LegacyDamageSource(
                Execution,
                Context,
                DamageSourceId,
                Tags | tags);
        }

        public CombatResolution Resolve(ICombatTarget target, DamageInfo damage)
        {
            if (!IsValid)
            {
                throw new InvalidOperationException(
                    "A valid legacy combat source is required to resolve damage.");
            }

            return Execution.Resolve(this, target, damage);
        }
    }

    public static class LegacyCombatTagUtility
    {
        public static CombatTags FromDamageType(DamageType damageType)
        {
            switch (damageType)
            {
                case DamageType.Fire:
                    return CombatTags.Fire;
                case DamageType.Poison:
                    return CombatTags.Poison;
                case DamageType.Bleed:
                    return CombatTags.Status | CombatTags.Periodic;
                default:
                    return CombatTags.None;
            }
        }
    }
}
