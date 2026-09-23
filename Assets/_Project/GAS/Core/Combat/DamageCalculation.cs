using System;
using System.Collections.Generic;

namespace MonsterSupergroup.GAS
{
    [Serializable] public sealed class DamageCalculationInput
    {
        public AttackStatsSnapshot stats;
        public AttackStatsMultipliers targetMultipliers;
        public float criticalRoll;
        // Empty means no target-dependent modifiers ran. Unknown modifiers are never replayed from their outputs.
        public ModifierEvidence[] modifiers;
    }

    [Serializable] public struct DamageCalculationResult
    {
        public int baseDamage, requestedDamage;
        public float criticalChance, criticalRoll, criticalMultiplier;
        public bool isCritical;
    }

    /// <summary>The live hit path and offline replay share these exact rounding and probability rules.</summary>
    public static class DamageCalculation
    {
        public static DamageCalculationResult Calculate(AttackStatsSnapshot stats, AttackStatsMultipliers target, float roll)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            float factor = target.damage >= 0f ? 1f + target.damage : 1f / (1f + Math.Abs(target.damage));
            var result = new DamageCalculationResult {
                baseDamage = ToInt(stats.DamageBeforeRounding * factor, true),
                criticalChance = Probability.Clamp01(stats.CritRate + target.critRate), criticalRoll = roll
            };
            result.isCritical = result.criticalChance > 0f && roll < result.criticalChance;
            result.criticalMultiplier = stats.CritDamageMultiplier + target.critDamage;
            if (result.criticalMultiplier < 0f) result.criticalMultiplier = 0f;
            result.requestedDamage = result.isCritical ? ToInt(result.baseDamage * result.criticalMultiplier, false) : result.baseDamage;
            return result;
        }

        public static DamageCalculationResult Replay(DamageCalculationInput input)
        {
            if (input == null || input.modifiers == null) throw new InvalidOperationException("MissingDamageModifierEvidence");
            if (input.modifiers.Length != 0)
                throw new InvalidOperationException("UnsupportedTargetModifier: " + input.modifiers[0].type);
            // Recompute the accumulator. Do not trust logged targetMultipliers as a modifier result.
            return Calculate(input.stats, new AttackStatsMultipliers(), input.criticalRoll);
        }

        private static int ToInt(float value, bool ceiling)
        {
            if (float.IsNaN(value) || value <= 0f) return 0;
            if (float.IsPositiveInfinity(value) || value >= int.MaxValue) return int.MaxValue;
            return ceiling ? (int)Math.Ceiling(value) : (int)value;
        }
    }

    [Serializable] public sealed class ModifierEvidence
    {
        public uint id;
        public string type;
        public float[] parameters;
        public bool supported;

        public static ModifierEvidence Capture(RuntimeEquipmentModifier modifier)
        {
            var values = new List<float>();
            for (int i = 0; i < 64 && modifier.Parameters.TryGetNumericParameter(i, out float value); i++) values.Add(value);
            return new ModifierEvidence { id = modifier.ID.Value, type = modifier.GetType().FullName,
                parameters = values.ToArray(), supported = IsSupportedStatic(modifier.GetType()) };
        }
        private static bool IsSupportedStatic(Type type) => type == typeof(DamageStatModifier) || type == typeof(SizeStatModifier)
            || type == typeof(SpeedStatModifier) || type == typeof(DurationStatModifier) || type == typeof(CritRateStatModifier)
            || type == typeof(CritMultiplierStatModifier) || type == typeof(ProjectileCountStatModifier) || type == typeof(KnockbackStatModifier);

        public StaticStatModifier CreateStatic()
        {
            if (parameters == null || parameters.Length != 1) throw new InvalidOperationException("UnsupportedModifierParameters: " + type);
            float value = parameters[0];
            // This explicit allowlist does not instantiate type names supplied by a log.
            StaticStatModifier modifier = id switch {
                DamageStatModifier.ModifierIdValue => new DamageStatModifier(new DamageStatModifierParameters(value)),
                SizeStatModifier.ModifierIdValue => new SizeStatModifier(new SizeStatModifierParameters(value)),
                SpeedStatModifier.ModifierIdValue => new SpeedStatModifier(new SpeedStatModifierParameters(value)),
                DurationStatModifier.ModifierIdValue => new DurationStatModifier(new DurationStatModifierParameters(value)),
                CritRateStatModifier.ModifierIdValue => new CritRateStatModifier(new CritRateStatModifierParameters(value)),
                CritMultiplierStatModifier.ModifierIdValue => new CritMultiplierStatModifier(new CritMultiplierStatModifierParameters(value)),
                ProjectileCountStatModifier.ModifierIdValue => new ProjectileCountStatModifier(new ProjectileCountStatModifierParameters(checked((int)value))),
                KnockbackStatModifier.ModifierIdValue => new KnockbackStatModifier(new KnockbackStatModifierParameters(value)),
                _ => throw new InvalidOperationException("UnsupportedModifier: " + type)
            };
            if (modifier.GetType().FullName != type) throw new InvalidOperationException("ModifierIdentityMismatch: " + type);
            return modifier;
        }
    }

    [Serializable] public struct StatRemapEvidence { public AttackStatType target, source; }
    [Serializable] public sealed class AttackStatsEvidenceInput
    {
        public AttackStats baseStats;
        public AttackStatsMultipliers globalMultipliers;
        public ModifierEvidence[] staticModifiers, dynamicModifiers;
        public StatRemapEvidence[] remaps;

        public AttackStatsSnapshot Rebuild()
        {
            if (staticModifiers == null || dynamicModifiers == null || remaps == null)
                throw new InvalidOperationException("MissingStatConstructionEvidence");
            if (dynamicModifiers.Length != 0) throw new InvalidOperationException("UnsupportedDynamicModifier: " + dynamicModifiers[0].type);
            var stats = new WeaponBehaviourStats(baseStats, globalMultipliers);
            foreach (var remap in remaps) stats.RemapStat(remap.target, remap.source);
            foreach (var modifier in staticModifiers) using (var runtime = modifier.CreateStatic()) runtime.Apply(stats);
            return stats.CreateSnapshot();
        }
    }

    /// <summary>Health is a diagnostic input, never authority to modify a combatant.</summary>
    public interface ICombatHealthEvidence
    {
        int DiagnosticHealth { get; }
        bool DiagnosticInvulnerable { get; }
    }

    [Serializable] public sealed class CalculationReplayState { public int version = 1; }
    public static class CombatCalculationEvidence
    {
        private static readonly object DamageEngine = new(), StatsEngine = new();
        public static int? Health(ICombatTarget target, CombatContext context = default)
        {
            if (!CombatEvidence.Enabled) return null;
            try { return (target as ICombatHealthEvidence)?.DiagnosticHealth; }
            catch (Exception error) { CombatEvidence.ReportCaptureFailure("Owner", "owner.health", error, context); return null; }
        }
        public static bool? Invulnerable(ICombatTarget target, CombatContext context = default)
        {
            if (!CombatEvidence.Enabled) return null;
            try { return (target as ICombatHealthEvidence)?.DiagnosticInvulnerable; }
            catch (Exception error) { CombatEvidence.ReportCaptureFailure("Owner", "owner.permission", error, context); return null; }
        }
        public static void Record(string domain, string stage, string operation, CombatContext context, object input, object output)
        {
            if (!CombatEvidence.Enabled) return;
            string engine = CombatEvidence.Register(domain == "damage" ? DamageEngine : StatsEngine, domain, _ => new CalculationReplayState());
            CombatEvidence.Write(new DiagnosticRecord { role = "Owner", stage = stage, engine = engine, operation = operation,
                eventId = context.EventId.Value.ToString(), rootEventId = context.RootEventId.Value.ToString(), parentEventId = context.ParentEventId.IsValid ? context.ParentEventId.Value.ToString() : null,
                source = context.SourcePlayerId, target = context.TargetEntityId, stateVersion = context.TargetStateVersion,
                outcome = "Resolved", input = input, after = output, critical = true, estimatedBytes = 2048 });
        }
    }
}
