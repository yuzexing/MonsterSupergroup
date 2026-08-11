using System;
using MonsterSupergroup.GAS;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Combat
{
    [DisallowMultipleComponent]
    public sealed class CombatantBehaviour : MonoBehaviour, ICombatTarget
    {
        [SerializeField, Min(1)] private int maxHealth = 100;

        private StatusController statusController;
        private bool isInitialized;

        public event Action<int, int> HealthChanged;

        /// <summary>The Boolean argument is true for status damage and false for direct damage.</summary>
        public event Action<DamageInfo, bool> DamageReceived;

        public int CurrentHealth { get; private set; }

        public int MaxHealth => maxHealth;

        public bool IsAlive => isInitialized && CurrentHealth > 0;

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
            HealthChanged?.Invoke(CurrentHealth, maxHealth);
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

            StatusTickCount++;
            ApplyDamage(tick.Damage, true);
        }

        private void OnDestroy()
        {
            statusController?.Clear();
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
