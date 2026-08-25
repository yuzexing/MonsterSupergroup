using System;

namespace MonsterSupergroup.GAS
{
    public enum CombatEventKind : byte
    {
        AttackStarted = 0,
        HitResolved = 1,
        DamageResolved = 2,
        PredictedLethalHit = 3,
        ConfirmedKill = 4
    }

    public readonly struct CombatResolution
    {
        public CombatResolution(
            CombatContext hitContext,
            CombatContext damageContext,
            CombatContext predictedLethalContext,
            DamageInfo resolvedDamage,
            DamageInfo predictedAppliedDamage,
            bool targetWasAlive,
            bool targetIsAlive)
        {
            HitContext = hitContext;
            DamageContext = damageContext;
            PredictedLethalContext = predictedLethalContext;
            ResolvedDamage = resolvedDamage;
            PredictedAppliedDamage = predictedAppliedDamage;
            TargetWasAlive = targetWasAlive;
            TargetIsAlive = targetIsAlive;
        }

        public CombatContext HitContext { get; }
        public CombatContext DamageContext { get; }
        public CombatContext PredictedLethalContext { get; }
        public DamageInfo ResolvedDamage { get; }
        public DamageInfo PredictedAppliedDamage { get; }
        public bool TargetWasAlive { get; }
        public bool TargetIsAlive { get; }

        public bool IsPredictedLethal => PredictedLethalContext.IsValid;
    }

    public readonly struct CombatEvent
    {
        public CombatEvent(
            CombatEventKind kind,
            CombatContext context,
            DamageInfo resolvedDamage = default,
            DamageInfo predictedAppliedDamage = default)
        {
            if (!context.IsValid)
            {
                throw new ArgumentException("Combat events require a valid context.", nameof(context));
            }

            Kind = kind;
            Context = context;
            ResolvedDamage = resolvedDamage;
            PredictedAppliedDamage = predictedAppliedDamage;
        }

        public CombatEventKind Kind { get; }
        public CombatContext Context { get; }
        public DamageInfo ResolvedDamage { get; }
        public DamageInfo PredictedAppliedDamage { get; }
    }

    public interface ICombatEventSink
    {
        void Publish(CombatEvent combatEvent);
    }

    internal sealed class NullCombatEventSink : ICombatEventSink
    {
        public static readonly NullCombatEventSink Instance = new NullCombatEventSink();

        private NullCombatEventSink()
        {
        }

        public void Publish(CombatEvent combatEvent)
        {
        }
    }
}
