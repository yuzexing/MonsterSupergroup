using System;
using MonsterSupergroup.GAS;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Combat
{
    [DisallowMultipleComponent]
    public sealed class CombatantBehaviour : MonoBehaviour, ICombatTarget, ICombatStateIdentity
    {
        [SerializeField, Min(1)] private int maxHealth = 100;

        private StatusController statusController;
        private bool isInitialized;
        private uint entityId;
        private uint stateVersion;
        private ICombatEventIdSource statusEventIds;
        private ICombatEventSink statusEventSink;

        public event Action<int, int> HealthChanged;

        /// <summary>The Boolean argument is true for status damage and false for direct damage.</summary>
        public event Action<DamageInfo, bool> DamageReceived;

        public int CurrentHealth { get; private set; }

        public int MaxHealth => maxHealth;

        public bool IsAlive => isInitialized && CurrentHealth > 0;

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

        public DamageInfo ReceiveDamage(DamageInfo requestedDamage)
        {
            EnsureInitialized();
            return ApplyDamage(requestedDamage, false);
        }

        public StatusApplicationResult ApplyStatus(StatusApplication application)
        {
            EnsureInitialized();
            return IsAlive
                ? statusController.Apply(application)
                : StatusApplicationResult.Rejected;
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
            PublishStatusDamage(tick, applied, wasAlive);
        }

        private void PublishStatusDamage(
            StatusTick tick,
            DamageInfo predictedApplied,
            bool targetWasAlive)
        {
            if (statusEventIds == null || statusEventSink == null ||
                tick.Damage.Value <= 0 || entityId == 0 ||
                tick.Instance.SourcePlayerId == 0 || tick.Instance.SourceEntityId == 0)
            {
                return;
            }

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

            CombatEventId eventId = statusEventIds.Next();
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
            statusEventSink.Publish(new CombatEvent(
                CombatEventKind.DamageResolved,
                damageContext,
                tick.Damage,
                predictedApplied));

            if (targetWasAlive && !IsAlive)
            {
                CombatContext lethalContext = damageContext.CreateChild(
                    statusEventIds.Next(),
                    CombatTags.PredictedLethalHit,
                    entityId,
                    stateVersion);
                statusEventSink.Publish(new CombatEvent(
                    CombatEventKind.PredictedLethalHit,
                    lethalContext,
                    tick.Damage,
                    predictedApplied));
            }
        }

        private void OnDestroy()
        {
            statusController?.Clear();
            ClearStatusCombatEvents();
            HealthChanged = null;
            DamageReceived = null;
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
