using System;
using MonsterSupergroup.GAS;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Combat
{
    [DisallowMultipleComponent]
    public sealed class CombatantBehaviour : MonoBehaviour, ICombatTarget, IStatusQuery,
        ICombatStateIdentity, ICombatLifecycleTarget
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

        public event Action<int, int> HealthChanged;

        /// <summary>The Boolean argument is true for status damage and false for direct damage.</summary>
        public event Action<DamageInfo, bool> DamageReceived;

        public event Action<StatusTick, DamageInfo> StatusDamageReceived;

        public event Action<PredictedLethalHit> PredictedLethalHitReceived;

        public event Action<ConfirmedKill> ConfirmedKillReceived;

        public int CurrentHealth { get; private set; }

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
                return false;
            }

            if (maximumHealth < 1 || health < 0 || health > maximumHealth)
            {
                throw new ArgumentOutOfRangeException(nameof(health));
            }

            maxHealth = maximumHealth;
            CurrentHealth = health;
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
            return true;
        }

        public void ResetCombatant()
        {
            Initialize(maxHealth);
        }

        public void ConfigureKillConfirmation(bool requireCanonicalConfirmation)
        {
            requiresCanonicalKillConfirmation = requireCanonicalConfirmation;
        }

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
            int appliedValue = IsAlive
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
                return;
            }

            bool wasAlive = IsAlive;
            StatusTickCount++;
            DamageInfo applied = ApplyDamage(tick.Damage, true);
            if (applied.Value > 0)
            {
                StatusDamageReceived?.Invoke(tick, applied);
            }
            PublishStatusDamage(tick, applied, wasAlive);
        }

        private void PublishStatusDamage(
            StatusTick tick,
            DamageInfo predictedApplied,
            bool targetWasAlive)
        {
            if (tick.Damage.Value <= 0)
            {
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
            activeEventSink.Publish(new CombatEvent(
                CombatEventKind.DamageResolved,
                damageContext,
                tick.Damage,
                predictedApplied));

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
