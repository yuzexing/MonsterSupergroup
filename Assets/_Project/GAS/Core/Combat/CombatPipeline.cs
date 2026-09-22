using System;

namespace MonsterSupergroup.GAS
{
    public sealed class CombatPipeline
    {
        private readonly RuntimeEquipmentModifiers modifiers;
        private readonly IRandomSource random;
        private readonly ICombatEventIdSource eventIds;
        private readonly ICombatEventSink eventSink;
        private readonly CombatTriggerGuard triggerGuard;
        private readonly ICombatTimeSource timeSource;

        public CombatPipeline(RuntimeEquipmentModifiers modifiers, IRandomSource random)
            : this(
                modifiers,
                random,
                new SequentialCombatEventIdSource(),
                NullCombatEventSink.Instance)
        {
        }

        public CombatPipeline(
            RuntimeEquipmentModifiers modifiers,
            IRandomSource random,
            ICombatEventIdSource eventIds,
            ICombatEventSink eventSink = null,
            CombatTriggerGuard triggerGuard = null,
            ICombatTimeSource timeSource = null)
        {
            this.modifiers = modifiers ?? throw new ArgumentNullException(nameof(modifiers));
            this.random = random ?? throw new ArgumentNullException(nameof(random));
            this.eventIds = eventIds ?? throw new ArgumentNullException(nameof(eventIds));
            this.eventSink = eventSink ?? NullCombatEventSink.Instance;
            this.triggerGuard = triggerGuard ?? new CombatTriggerGuard();
            this.timeSource = timeSource ?? MonotonicCombatTimeSource.Instance;
        }

        public AttackSnapshot BeginAttack(
            IWeaponRuntime weapon,
            AttackStatsMultipliers globalMultipliers = null)
        {
            return BeginAttack(weapon, CombatTags.Attack, globalMultipliers);
        }

        public AttackSnapshot BeginAttack(
            IWeaponRuntime weapon,
            CombatTags tags,
            AttackStatsMultipliers globalMultipliers = null)
        {
            if (weapon == null)
            {
                throw new ArgumentNullException(nameof(weapon));
            }

            uint sourcePlayerId = 0;
            uint sourceEntityId = 0;
            if (weapon is ICombatContextSource source)
            {
                sourcePlayerId = source.SourcePlayerId;
                sourceEntityId = source.SourceEntityId;
            }

            CombatContext context = CombatContext.CreateRoot(
                eventIds.Next(),
                sourcePlayerId,
                sourceEntityId,
                weapon.CombatId,
                tags | CombatTags.Attack);
            return BeginAttack(weapon, context, globalMultipliers);
        }

        public AttackSnapshot BeginAttack(
            IWeaponRuntime weapon,
            CombatContext context,
            AttackStatsMultipliers globalMultipliers = null)
        {
            if (weapon == null)
            {
                throw new ArgumentNullException(nameof(weapon));
            }

            if (!context.IsValid)
            {
                throw new ArgumentException("Attack context must be valid.", nameof(context));
            }

            AttackStatsSnapshot stats = RefreshStats(weapon, globalMultipliers);
            RuntimeModifierExecutionSnapshot execution =
                modifiers.CaptureExecutionSnapshot();
            var attack = new AttackSnapshot(weapon, stats, context, execution);
            if (CombatEvidence.Enabled)
            {
                try
                {
                    var statInput = new AttackStatsEvidenceInput { baseStats = weapon.Stats.BaseStats,
                        globalMultipliers = weapon.Stats.GlobalStatsMultipliers.Clone(), remaps = weapon.Stats.CaptureDiagnosticRemaps(),
                        staticModifiers = CaptureModifiers(modifiers.StaticModifiers), dynamicModifiers = CaptureModifiers(modifiers.DynamicModifiers) };
                    CombatCalculationEvidence.Record("weapon_stats", "owner.attack_stats", "Rebuild", context, statInput, stats);
                }
                catch (Exception error) { CombatEvidence.Event("Owner", "owner.attack_stats", "CaptureFailed", error.GetType().Name,
                    context.EventId.Value, context.SourcePlayerId, root: context.RootEventId.Value); }
            }
            try
            {
                eventSink.Publish(new CombatEvent(CombatEventKind.AttackStarted, context));
                return attack;
            }
            catch
            {
                attack.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Rebuilds the weapon's derived stat layers without creating a gameplay event.
        /// </summary>
        public AttackStatsSnapshot RefreshStats(
            IWeaponRuntime weapon,
            AttackStatsMultipliers globalMultipliers = null)
        {
            if (weapon == null)
            {
                throw new ArgumentNullException(nameof(weapon));
            }

            WeaponBehaviourStats stats = weapon.Stats ??
                throw new ArgumentException("Weapon runtime must provide stats.", nameof(weapon));

            stats.ResetBase();
            for (int i = 0; i < modifiers.StaticModifiers.Count; i++)
            {
                modifiers.StaticModifiers[i].Apply(stats);
            }

            stats.ResetGlobal();
            if (globalMultipliers != null)
            {
                stats.GlobalStatsMultipliers.CopyFrom(globalMultipliers);
            }

            stats.ResetDynamic();
            for (int i = 0; i < modifiers.DynamicModifiers.Count; i++)
            {
                modifiers.DynamicModifiers[i].Apply(stats, weapon);
            }

            return stats.CreateSnapshot();
        }

        /// <summary>
        /// Compatibility API. Returns damage accepted by the local predicted target.
        /// Use ResolveHitDetailed when building a CombatResult for the server.
        /// </summary>
        public DamageInfo ResolveHit(
            AttackSnapshot attack,
            ICombatTarget target,
            float onHitChanceMultiplier = 1f,
            float onKillChanceMultiplier = 1f,
            float burnDamageMultiplier = 0f)
        {
            return ResolveHitDetailed(
                attack,
                target,
                onHitChanceMultiplier,
                onKillChanceMultiplier,
                burnDamageMultiplier).PredictedAppliedDamage;
        }

        /// <summary>
        /// Starts an attack whose damage was authored by a compatibility layer rather
        /// than an <see cref="IWeaponRuntime"/>. The returned context can be shared by
        /// every hit produced by that attack.
        /// </summary>
        public CombatContext BeginExternalAttack(
            uint sourcePlayerId,
            uint sourceEntityId,
            uint abilityId,
            CombatTags tags = CombatTags.Attack)
        {
            CombatContext context = CombatContext.CreateRoot(
                eventIds.Next(),
                sourcePlayerId,
                sourceEntityId,
                abilityId,
                tags | CombatTags.Attack);
            eventSink.Publish(new CombatEvent(CombatEventKind.AttackStarted, context));
            return context;
        }

        /// <summary>
        /// Applies damage calculated by a legacy gameplay system through the same
        /// target mutation and combat-event path used by native GAS weapons.
        /// Legacy modifiers remain the caller's responsibility and are not run here.
        /// </summary>
        public CombatResolution ResolvePrecomputedHit(
            CombatContext attackContext,
            ICombatTarget target,
            DamageInfo resolvedDamage,
            DamageType presentationDamageType = DamageType.Normal)
        {
            if (!attackContext.IsValid)
            {
                throw new ArgumentException(
                    "Precomputed hits require a valid source context.",
                    nameof(attackContext));
            }

            return ResolveDamageCore(
                attackContext,
                target,
                resolvedDamage,
                null,
                1f,
                1f,
                0f,
                presentationDamageType);
        }

        public CombatResolution ResolveHitDetailed(
            AttackSnapshot attack,
            ICombatTarget target,
            float onHitChanceMultiplier = 1f,
            float predictedLethalChanceMultiplier = 1f,
            float burnDamageMultiplier = 0f,
            DamageType? presentationDamageType = null)
        {
            if (attack == null)
            {
                throw new ArgumentNullException(nameof(attack));
            }

            attack.EnsureAlive();

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            ValidateFinite(onHitChanceMultiplier, nameof(onHitChanceMultiplier));
            ValidateFinite(
                predictedLethalChanceMultiplier,
                nameof(predictedLethalChanceMultiplier));
            ValidateFinite(burnDamageMultiplier, nameof(burnDamageMultiplier));

            if (!target.IsAlive)
            {
                if (CombatEvidence.Enabled) CombatEvidence.Event("Owner", "owner.hit_filter", "Ignored", "TargetNotAlive",
                    attack.Context.EventId.Value, attack.Context.SourcePlayerId, target is ICombatStateIdentity filtered ? filtered.EntityId : 0,
                    root: attack.Context.RootEventId.Value);
                var zero = new DamageInfo(attack.CombatId, 0, false);
                return new CombatResolution(
                    CombatContext.None,
                    CombatContext.None,
                    CombatContext.None,
                    zero,
                    zero,
                    false,
                    false);
            }

            var targetMultipliers = new AttackStatsMultipliers();
            DynamicOnDamageModifier[] onDamageModifiers =
                attack.Execution.DynamicOnDamageModifiers;
            for (int i = 0; i < onDamageModifiers.Length; i++)
            {
                onDamageModifiers[i].Apply(targetMultipliers, target);
            }

            float criticalChance = Probability.Clamp01(attack.Stats.CritRate + targetMultipliers.critRate);
            float criticalRoll = criticalChance > 0f ? random.Next01() : -1f;
            DamageCalculationResult calculation = DamageCalculation.Calculate(attack.Stats, targetMultipliers, criticalRoll);
            DamageCalculationInput calculationInput = null;
            if (CombatEvidence.Enabled)
            {
                try { calculationInput = new DamageCalculationInput { stats = attack.Stats, targetMultipliers = targetMultipliers.Clone(),
                    criticalRoll = criticalRoll, modifiers = CaptureModifiers(onDamageModifiers) }; }
                catch (Exception error) { CombatEvidence.Event("Owner", "owner.damage_calculation", "CaptureFailed", error.GetType().Name,
                    attack.Context.EventId.Value, attack.Context.SourcePlayerId, root: attack.Context.RootEventId.Value); }
            }

            var resolvedDamage = new DamageInfo(
                attack.CombatId,
                calculation.requestedDamage,
                calculation.isCritical);
            return ResolveDamageCore(
                attack.Context,
                target,
                resolvedDamage,
                attack,
                onHitChanceMultiplier,
                predictedLethalChanceMultiplier,
                burnDamageMultiplier,
                presentationDamageType ?? attack.Stats.DamageType,
                calculationInput, calculation);
        }

        private CombatResolution ResolveDamageCore(
            CombatContext sourceContext,
            ICombatTarget target,
            DamageInfo resolvedDamage,
            AttackSnapshot attack,
            float onHitChanceMultiplier,
            float predictedLethalChanceMultiplier,
            float burnDamageMultiplier,
            DamageType presentationDamageType,
            DamageCalculationInput calculationInput = null,
            DamageCalculationResult calculation = default)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            bool wasAlive = target.IsAlive;
            if (!wasAlive)
            {
                var zero = new DamageInfo(resolvedDamage.Id, 0, false);
                return new CombatResolution(
                    CombatContext.None,
                    CombatContext.None,
                    CombatContext.None,
                    zero,
                    zero,
                    false,
                    false);
            }

            CombatEventId rootEventId = sourceContext.RootEventId;
            triggerGuard.BeginRootScope(rootEventId);
            try
            {
                uint targetEntityId = 0;
                uint targetStateVersion = 0;
                if (target is ICombatStateIdentity identity)
                {
                    targetEntityId = identity.EntityId;
                    targetStateVersion = identity.StateVersion;
                }

                CombatContext hitContext = sourceContext.CreateChild(
                    eventIds.Next(),
                    CombatTags.Hit,
                    targetEntityId,
                    targetStateVersion);
                eventSink.Publish(new CombatEvent(
                    CombatEventKind.HitResolved,
                    hitContext));

                CombatTags damageTags = CombatTags.Damage;
                if (resolvedDamage.IsCritical)
                {
                    damageTags |= CombatTags.Critical;
                }

                CombatContext damageContext = hitContext.CreateChild(
                    eventIds.Next(),
                    damageTags,
                    targetEntityId,
                    targetStateVersion);
                if (calculationInput != null) CombatCalculationEvidence.Record("damage", "owner.damage_calculation", "Calculate",
                    damageContext, calculationInput, calculation);
                int? previousHealth = CombatCalculationEvidence.Health(target);
                if (CombatEvidence.Enabled) CombatEvidence.Event("Owner", "owner.hit", "Applying", null,
                    damageContext.EventId.Value, damageContext.SourcePlayerId, targetEntityId, resolvedDamage,
                    new { alive = wasAlive, health = previousHealth, version = targetStateVersion,
                        invulnerable = CombatCalculationEvidence.Invulnerable(target) }, root: damageContext.RootEventId.Value, parent: damageContext.ParentEventId.Value);
                DamageInfo predictedAppliedDamage = target.ReceiveDamage(resolvedDamage);
                if (CombatEvidence.Enabled) CombatEvidence.Event("Owner", "owner.hit", "Applied", null,
                    damageContext.EventId.Value, damageContext.SourcePlayerId, targetEntityId, resolvedDamage,
                    new { alive = wasAlive, health = previousHealth, version = targetStateVersion },
                    new { alive = target.IsAlive, health = CombatCalculationEvidence.Health(target), applied = predictedAppliedDamage,
                        version = (target as ICombatStateIdentity)?.StateVersion },
                    damageContext.RootEventId.Value, damageContext.ParentEventId.Value);
                eventSink.Publish(new CombatEvent(
                    CombatEventKind.DamageResolved,
                    damageContext,
                    resolvedDamage,
                    predictedAppliedDamage,
                    presentationDamageType: presentationDamageType));

                if (attack != null && predictedAppliedDamage.Value > 0)
                {
                    OnHitModifier[] onHitModifiers = attack.Execution.OnHitModifiers;
                    for (int i = 0; i < onHitModifiers.Length; i++)
                    {
                        OnHitModifier modifier = onHitModifiers[i];
                        if (!triggerGuard.TryEnter(
                                damageContext,
                                modifier.ID,
                                targetEntityId,
                                modifier.GetTriggerPolicy(),
                                timeSource.CurrentTimeSeconds))
                        {
                            continue;
                        }

                        var onHitArgs = new OnHitModifierArgs(
                            damageContext.WithBuild(modifier.ID.Value),
                            target,
                            attack,
                            resolvedDamage,
                            predictedAppliedDamage,
                            random,
                            onHitChanceMultiplier,
                            burnDamageMultiplier);
                        modifier.Apply(onHitArgs);
                    }
                }

                CombatContext predictedLethalContext = CombatContext.None;
                if (wasAlive && !target.IsAlive)
                {
                    predictedLethalContext = damageContext.CreateChild(
                        eventIds.Next(),
                        CombatTags.PredictedLethalHit,
                        targetEntityId,
                        targetStateVersion);
                    eventSink.Publish(new CombatEvent(
                        CombatEventKind.PredictedLethalHit,
                        predictedLethalContext,
                        resolvedDamage,
                        predictedAppliedDamage,
                        presentationDamageType: presentationDamageType));

                    if (attack != null)
                    {
                        OnPredictedLethalHitModifier[] lethalModifiers =
                            attack.Execution.PredictedLethalHitModifiers;
                        for (int i = 0; i < lethalModifiers.Length; i++)
                        {
                            OnPredictedLethalHitModifier modifier =
                                lethalModifiers[i];
                            if (!triggerGuard.TryEnter(
                                    predictedLethalContext,
                                    modifier.ID,
                                    targetEntityId,
                                    modifier.GetTriggerPolicy(),
                                    timeSource.CurrentTimeSeconds))
                            {
                                continue;
                            }

                            var args = new OnPredictedLethalHitModifierArgs(
                                predictedLethalContext.WithBuild(modifier.ID.Value),
                                target,
                                attack,
                                resolvedDamage,
                                predictedAppliedDamage,
                                random,
                                predictedLethalChanceMultiplier);
                            modifier.Apply(args);
                        }
                    }

                    if (target is ICombatLifecycleTarget lifecycleTarget)
                    {
                        lifecycleTarget.ReceivePredictedLethalHit(
                            new PredictedLethalHit(
                                predictedLethalContext,
                                resolvedDamage,
                                predictedAppliedDamage));
                    }
                }

                return new CombatResolution(
                    hitContext,
                    damageContext,
                    predictedLethalContext,
                    resolvedDamage,
                    predictedAppliedDamage,
                    wasAlive,
                    target.IsAlive);
            }
            finally
            {
                triggerGuard.EndRootScope(rootEventId);
            }
        }

        private static ModifierEvidence[] CaptureModifiers<T>(System.Collections.Generic.IReadOnlyList<T> values) where T : RuntimeEquipmentModifier
        {
            var result = new ModifierEvidence[values.Count];
            for (int i = 0; i < result.Length; i++) result[i] = ModifierEvidence.Capture(values[i]);
            return result;
        }

        private static void ValidateFinite(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(parameterName, "Multiplier must be finite.");
            }
        }
    }
}
