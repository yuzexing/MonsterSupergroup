using System;
using MonsterSupergroup.GAS;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Combat
{
    [DisallowMultipleComponent]
    public sealed class CombatantBehaviour : MonoBehaviour, ICombatTarget, IStatusQuery,
        ICombatStateIdentity, ICombatLifecycleTarget, ICombatHealthEvidence
    {
        [SerializeField, Min(1)] private int maxHealth = 100;

        private StatusController statusController;
        private bool isInitialized;
        private uint entityId;
        private uint stateVersion;
        private ICombatEventIdSource statusEventIds;
        private ICombatEventSink statusEventSink;
        private ICombatEventIdSource fallbackStatusEventIds;
        private bool requiresCanonicalKillConfirmation;
        private bool executesCanonicalConsequences = true;
        private bool predictedLethalRaised;
        private bool confirmedKillRaised;
        private bool upgradeSelectionInvulnerable;
        private bool canonicalInvulnerable;
        private bool ultimateInvulnerable;

        public bool IsInvulnerable => upgradeSelectionInvulnerable || canonicalInvulnerable || ultimateInvulnerable;
        public int DiagnosticHealth => CurrentHealth;
        public bool DiagnosticInvulnerable => IsInvulnerable;

        public void SetUltimateInvulnerable(bool value)
        {
            int previous = InvulnerabilityFlags;
            ultimateInvulnerable = value;
            TraceInvulnerability(previous, "Ultimate", value);
        }

        public void SetUpgradeSelectionInvulnerable(bool value)
        {
            int previous = InvulnerabilityFlags;
            upgradeSelectionInvulnerable = value;
            TraceInvulnerability(previous, "UpgradeSelection", value);
        }

        public void SetCanonicalInvulnerable(bool value)
        {
            int previous = InvulnerabilityFlags;
            canonicalInvulnerable = value;
            TraceInvulnerability(previous, "Canonical", value);
        }

        private int InvulnerabilityFlags => (upgradeSelectionInvulnerable ? 1 : 0) | (canonicalInvulnerable ? 2 : 0) | (ultimateInvulnerable ? 4 : 0);
        private void TraceInvulnerability(int previous, string flag, bool value)
        {
            if (!CombatEvidence.Enabled || previous == InvulnerabilityFlags) return;
            CombatEvidence.Write(new DiagnosticRecord { role = "Entity", stage = "entity.permission", outcome = "Changed", reason = flag,
                target = entityId, stateVersion = stateVersion, input = new { flag, value }, before = previous,
                after = new { flags = InvulnerabilityFlags, upgradeSelectionInvulnerable, canonicalInvulnerable, ultimateInvulnerable }, critical = true });
        }

        public event Action<int, int> HealthChanged;

        /// <summary>The Boolean argument is true for status damage and false for direct damage.</summary>
        public event Action<DamageInfo, bool> DamageReceived;

        public event Action<StatusTick, DamageInfo> StatusDamageReceived;

        public event Action<PredictedLethalHit> PredictedLethalHitReceived;

        public event Action<ConfirmedKill> ConfirmedKillReceived;

        public int CurrentHealth { get; private set; }

        public bool IsInitialized => isInitialized;

        public int MaxHealth => maxHealth;

        public bool IsAlive => isInitialized && CurrentHealth > 0;

        public bool RequiresCanonicalKillConfirmation =>
            requiresCanonicalKillConfirmation;

        public bool ExecutesCanonicalConsequences => executesCanonicalConsequences;

        public uint EntityId => entityId;

        public uint StateVersion => stateVersion;

        public long DirectDamageTaken { get; private set; }

        public long StatusDamageTaken { get; private set; }

        public int StatusTickCount { get; private set; }

        public StatusController StatusController
        {
            get
            {
                EnsureInitialized();
                return statusController;
            }
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        public void Initialize()
        {
            Initialize(maxHealth);
        }

        public void Initialize(int maximumHealth)
        {
            if (maximumHealth < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumHealth),
                    "Maximum health must be at least one.");
            }

            if (statusController == null)
            {
                statusController = new StatusController(ReceiveStatusTick);
            }
            else
            {
                statusController.Clear();
            }

            maxHealth = maximumHealth;
            CurrentHealth = maximumHealth;
            DirectDamageTaken = 0;
            StatusDamageTaken = 0;
            StatusTickCount = 0;
            predictedLethalRaised = false;
            confirmedKillRaised = false;
            isInitialized = true;
            stateVersion = 0;
            HealthChanged?.Invoke(CurrentHealth, maxHealth);
        }

        public void ConfigureEntityId(uint value)
        {
            if (value == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            entityId = value;
        }

        public bool ApplyCanonicalHealth(
            int health,
            int maximumHealth,
            uint version)
        {
            EnsureInitialized();
            if (version < stateVersion)
            {
                TraceCanonicalHealth(health, maximumHealth, version, CurrentHealth, stateVersion, "Ignored", "StaleStateVersion");
                return false;
            }

            if (maximumHealth < 1 || health < 0 || health > maximumHealth)
            {
                TraceCanonicalHealth(health, maximumHealth, version, CurrentHealth, stateVersion, "Rejected", "InvalidHealthRange");
                throw new ArgumentOutOfRangeException(nameof(health));
            }

            int previousHealth = CurrentHealth;
            uint previousVersion = stateVersion;
            bool keepPredictedDeath = clientFinalDeath && predictedLethalRaised && CurrentHealth == 0;
            maxHealth = maximumHealth;
            // Enemy outcomes are client-final. An older in-flight positive HP update
            // cannot undo a locally declared death while its receipt is in flight.
            CurrentHealth = clientFinalDeath && predictedLethalRaised && CurrentHealth == 0 ? 0 : health;
            stateVersion = version;
            if (CurrentHealth > 0 && !confirmedKillRaised)
            {
                predictedLethalRaised = false;
            }
            if (CurrentHealth == 0)
            {
                statusController.Clear();
            }

            HealthChanged?.Invoke(CurrentHealth, maxHealth);
            TraceCanonicalHealth(health, maximumHealth, version, previousHealth, previousVersion, "Applied",
                keepPredictedDeath && health > 0 ? "PredictedDeathRetained" : "CanonicalHealthApplied");
            return true;
        }

        private void TraceCanonicalHealth(int health, int maximum, uint version, int previousHealth, uint previousVersion, string outcome, string reason)
        {
            if (!CombatEvidence.Enabled) return;
            CombatEvidence.Write(new DiagnosticRecord { role = "Replica", stage = "entity.canonical_health", outcome = outcome, reason = reason,
                target = entityId, stateVersion = version, critical = outcome != "Applied" || health == 0 || previousHealth == 0,
                input = new { canonicalHealth = health, canonicalMaximum = maximum, canonicalVersion = version },
                before = new { localHealth = previousHealth, version = previousVersion },
                after = new { localHealth = CurrentHealth, localMaximum = maxHealth, version = stateVersion,
                    predictedLethalRaised, confirmedKillRaised, clientFinalDeath } });
        }

        public void ResetCombatant()
        {
            Initialize(maxHealth);
        }

        public void ConfigureKillConfirmation(bool requireCanonicalConfirmation)
        {
            requiresCanonicalKillConfirmation = requireCanonicalConfirmation;
        }

        private bool clientFinalDeath;
        public void ConfigureClientFinalDeath(bool enabled) => clientFinalDeath = enabled;

        public void ConfigureCanonicalConsequenceExecution(bool canExecute)
        {
            executesCanonicalConsequences = canExecute;
        }

        public void ReceivePredictedLethalHit(PredictedLethalHit hit)
        {
            EnsureInitialized();
            if (predictedLethalRaised)
            {
                return;
            }

            predictedLethalRaised = true;
            PredictedLethalHitReceived?.Invoke(hit);
            if (!requiresCanonicalKillConfirmation)
            {
                ReceiveConfirmedKill(new ConfirmedKill
                {
                    CauseEventId = hit.Context.EventId.Value,
                    KillerPlayerId = hit.Context.SourcePlayerId,
                    TargetEntityId = entityId != 0u
                        ? entityId
                        : hit.Context.TargetEntityId,
                    TargetStateVersion = stateVersion
                });
            }
        }

        public void ReceiveConfirmedKill(ConfirmedKill kill)
        {
            EnsureInitialized();
            if (confirmedKillRaised ||
                (entityId != 0u && kill.TargetEntityId != 0u &&
                 kill.TargetEntityId != entityId))
            {
                return;
            }

            confirmedKillRaised = true;
            ConfirmedKillReceived?.Invoke(kill);
        }

        public DamageInfo ReceiveDamage(DamageInfo requestedDamage)
        {
            EnsureInitialized();
            return ApplyDamage(requestedDamage, false);
        }

        public int RestoreHealth(int requestedHealth)
        {
            EnsureInitialized();
            if (requestedHealth <= 0 || !IsAlive || CurrentHealth >= maxHealth)
            {
                return 0;
            }

            int restoredHealth = Math.Min(requestedHealth, maxHealth - CurrentHealth);
            CurrentHealth += restoredHealth;
            HealthChanged?.Invoke(CurrentHealth, maxHealth);
            return restoredHealth;
        }

        public bool SetMaximumHealthPreservingMissingHealth(int maximumHealth)
        {
            EnsureInitialized();
            if (maximumHealth < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumHealth),
                    "Maximum health must be at least one.");
            }

            if (maximumHealth == maxHealth)
            {
                return false;
            }

            int missingHealth = maxHealth - CurrentHealth;
            maxHealth = maximumHealth;
            CurrentHealth = Math.Max(0, maxHealth - missingHealth);
            if (CurrentHealth == 0)
            {
                statusController.Clear();
            }

            HealthChanged?.Invoke(CurrentHealth, maxHealth);
            return true;
        }

        public StatusApplicationResult ApplyStatus(StatusApplication application)
        {
            EnsureInitialized();
            return IsAlive
                ? statusController.Apply(application)
                : StatusApplicationResult.Rejected;
        }

        public bool HasStatus(EnemyStatusID statusId)
        {
            EnsureInitialized();
            return statusController.Has(statusId);
        }

        public bool HasStatusFromSource(EnemyStatusID statusId, uint sourcePlayerId)
        {
            EnsureInitialized();
            return statusController.HasFromSource(statusId, sourcePlayerId);
        }

        public int GetStatusStackCount(EnemyStatusID statusId)
        {
            EnsureInitialized();
            return statusController.GetStackCount(statusId);
        }

        public System.Collections.Generic.IReadOnlyList<StatusInstance> GetStatusInstances(
            EnemyStatusID statusId)
        {
            EnsureInitialized();
            return statusController.GetInstances(statusId);
        }

        public void AdvanceStatuses(float deltaSeconds)
        {
            EnsureInitialized();
            statusController.Advance(deltaSeconds);
        }

        public void ConfigureStatusExecution(IStatusExecutionPolicy executionPolicy)
        {
            EnsureInitialized();
            statusController.SetExecutionPolicy(executionPolicy);
        }

        public void ConfigureStatusInstanceIds(IStatusInstanceIdSource instanceIds)
        {
            EnsureInitialized();
            statusController.SetInstanceIdSource(instanceIds);
        }

        public void ConfigureStatusCombatEvents(
            ICombatEventIdSource eventIds,
            ICombatEventSink eventSink)
        {
            statusEventIds = eventIds ?? throw new ArgumentNullException(nameof(eventIds));
            statusEventSink = eventSink ?? throw new ArgumentNullException(nameof(eventSink));
        }

        public void ClearStatusCombatEvents(ICombatEventSink expectedSink = null)
        {
            if (expectedSink != null && !ReferenceEquals(statusEventSink, expectedSink))
            {
                return;
            }

            statusEventIds = null;
            statusEventSink = null;
        }

        private DamageInfo ApplyDamage(DamageInfo requestedDamage, bool isStatusDamage)
        {
            int appliedValue = IsAlive && !IsInvulnerable
                ? Math.Min(CurrentHealth, requestedDamage.Value)
                : 0;
            var appliedDamage = new DamageInfo(
                requestedDamage.Id,
                appliedValue,
                requestedDamage.IsCritical);

            if (appliedValue == 0)
            {
                return appliedDamage;
            }

            CurrentHealth -= appliedValue;
            // Local prediction does not advance the canonical version. The network
            // replica keeps the last observed server StateVersion separately.
            if (isStatusDamage)
            {
                StatusDamageTaken += appliedValue;
            }
            else
            {
                DirectDamageTaken += appliedValue;
            }

            if (CurrentHealth == 0)
            {
                statusController.Clear();
            }

            DamageReceived?.Invoke(appliedDamage, isStatusDamage);
            HealthChanged?.Invoke(CurrentHealth, maxHealth);
            return appliedDamage;
        }

        private void ReceiveStatusTick(StatusTick tick)
        {
            if (!IsAlive)
            {
                if (CombatEvidence.Enabled) RecordStatusDamage(tick, CombatContext.None, default, CurrentHealth, "Ignored", "TargetNotAlive");
                return;
            }

            bool wasAlive = IsAlive;
            int previousHealth = CurrentHealth;
            StatusTickCount++;
            DamageInfo applied = ApplyDamage(tick.Damage, true);
            if (applied.Value > 0)
            {
                StatusDamageReceived?.Invoke(tick, applied);
            }
            PublishStatusDamage(tick, applied, wasAlive, previousHealth);
        }

        private void PublishStatusDamage(
            StatusTick tick,
            DamageInfo predictedApplied,
            bool targetWasAlive, int previousHealth)
        {
            if (tick.Damage.Value <= 0)
            {
                if (CombatEvidence.Enabled) RecordStatusDamage(tick, CombatContext.None, predictedApplied, previousHealth, "Ignored", "NonPositiveTickDamage");
                return;
            }

            ICombatEventIdSource activeEventIds = statusEventIds ??
                (fallbackStatusEventIds ??= new SequentialCombatEventIdSource());
            ICombatEventSink activeEventSink = statusEventSink ??
                NullCombatEventSink.Instance;

            CombatTags tags = CombatTags.Status | CombatTags.Periodic | CombatTags.Damage;
            switch (tick.StatusId)
            {
                case EnemyStatusID.Poison:
                    tags |= CombatTags.Poison;
                    break;
                case EnemyStatusID.Burn:
                    tags |= CombatTags.Burn | CombatTags.Fire;
                    break;
            }

            CombatEventId eventId = activeEventIds.Next();
            CombatContext source = tick.Instance.SourceContext;
            CombatContext damageContext = source.IsValid
                ? source.CreateChild(eventId, tags, entityId, stateVersion)
                : new CombatContext(
                    eventId,
                    eventId,
                    CombatEventId.None,
                    eventId.Sequence,
                    0,
                    tick.Instance.SourcePlayerId,
                    tick.Instance.SourceEntityId,
                    entityId,
                    0,
                    0,
                    tags,
                    stateVersion);
            if (CombatEvidence.Enabled) RecordStatusDamage(tick, damageContext, predictedApplied, previousHealth,
                predictedApplied.Value > 0 ? "Applied" : "Ignored", predictedApplied.Value > 0 ? "PredictedDamageApplied" : "InvulnerableOrDead");
            activeEventSink.Publish(new CombatEvent(
                CombatEventKind.DamageResolved,
                damageContext,
                tick.Damage,
                predictedApplied,
                tick.Instance.InstanceId,
                tick.Instance.ApplicationRevision,
                DamageTypeUtility.FromStatus(tick.StatusId)));

            if (targetWasAlive && !IsAlive)
            {
                CombatContext lethalContext = damageContext.CreateChild(
                    activeEventIds.Next(),
                    CombatTags.PredictedLethalHit,
                    entityId,
                    stateVersion);
                activeEventSink.Publish(new CombatEvent(
                    CombatEventKind.PredictedLethalHit,
                    lethalContext,
                    tick.Damage,
                    predictedApplied));
                ReceivePredictedLethalHit(new PredictedLethalHit(
                    lethalContext,
                    tick.Damage,
                    predictedApplied));
            }
        }

        private void RecordStatusDamage(StatusTick tick, CombatContext damageContext, DamageInfo applied, int previousHealth, string outcome, string reason)
        {
            CombatContext source = tick.Instance.SourceContext;
            CombatEvidence.Write(new DiagnosticRecord { role = "Owner", stage = "owner.dot_damage", outcome = outcome, reason = reason,
                engine = CombatEvidence.CurrentEngine,
                eventId = damageContext.IsValid ? damageContext.EventId.Value.ToString() : null,
                rootEventId = source.IsValid ? source.RootEventId.Value.ToString() : null,
                parentEventId = source.IsValid ? source.EventId.Value.ToString() : null,
                source = tick.Instance.SourcePlayerId, target = entityId, stateVersion = stateVersion,
                statusInstanceId = tick.InstanceId.Value.ToString(), applicationRevision = tick.Instance.ApplicationRevision, tickIndex = tick.TickIndex,
                input = tick, before = new { health = previousHealth, invulnerable = IsInvulnerable },
                after = new { health = CurrentHealth, applied, alive = IsAlive }, critical = true, estimatedBytes = 1280 });
        }

        private void OnDestroy()
        {
            statusController?.Clear();
            ClearStatusCombatEvents();
            fallbackStatusEventIds = null;
            HealthChanged = null;
            DamageReceived = null;
            StatusDamageReceived = null;
            PredictedLethalHitReceived = null;
            ConfirmedKillReceived = null;
            isInitialized = false;
        }

        private void EnsureInitialized()
        {
            if (!isInitialized)
            {
                Initialize(maxHealth);
            }
        }
    }
}
