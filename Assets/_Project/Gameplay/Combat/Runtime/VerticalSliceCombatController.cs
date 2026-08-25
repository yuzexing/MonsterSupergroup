using System;
using MonsterSupergroup.GAS;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Combat
{
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class VerticalSliceCombatController : MonoBehaviour
    {
        [SerializeField] private WeaponRuntimeBehaviour weapon;
        [SerializeField] private CombatantBehaviour target;
        [SerializeField] private bool autoAttack = true;
        [SerializeField] private float onHitChanceMultiplier = 1f;
        [SerializeField] private float onKillChanceMultiplier = 1f;
        [SerializeField] private float burnDamageMultiplier = 0f;

        private float elapsedSinceAttack;

        public WeaponRuntimeBehaviour Weapon => weapon;

        public CombatantBehaviour Target => target;

        public bool AutoAttack
        {
            get => autoAttack;
            set => autoAttack = value;
        }

        public DamageInfo LastDamage { get; private set; }

        public float AttackInterval
        {
            get
            {
                EnsureConfigured();
                float speed = weapon.Stats.SpeedValue;
                return IsUsableSpeed(speed) ? 1f / speed : float.PositiveInfinity;
            }
        }

        public void Configure(WeaponRuntimeBehaviour sourceWeapon, CombatantBehaviour combatTarget)
        {
            if (sourceWeapon == null)
            {
                throw new ArgumentNullException(nameof(sourceWeapon));
            }

            if (combatTarget == null)
            {
                throw new ArgumentNullException(nameof(combatTarget));
            }

            weapon = sourceWeapon;
            target = combatTarget;
            elapsedSinceAttack = 0f;
            LastDamage = new DamageInfo(sourceWeapon.CombatId, 0, false);
        }

        public DamageInfo AttackOnce()
        {
            EnsureConfigured();
            if (!target.IsAlive)
            {
                LastDamage = new DamageInfo(weapon.CombatId, 0, false);
                return LastDamage;
            }

            LastDamage = weapon.Attack(
                target,
                onHitChanceMultiplier,
                onKillChanceMultiplier,
                burnDamageMultiplier);
            return LastDamage;
        }

        public void ResetEncounter()
        {
            EnsureConfigured();
            target.ResetCombatant();
            elapsedSinceAttack = 0f;
            LastDamage = new DamageInfo(weapon.CombatId, 0, false);
        }

        public void Tick(float deltaSeconds)
        {
            if (float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds) || deltaSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(deltaSeconds),
                    "Delta time must be finite and non-negative.");
            }

            EnsureConfigured();
            if (!target.IsAlive)
            {
                return;
            }

            elapsedSinceAttack += deltaSeconds;
            while (target.IsAlive)
            {
                float interval = AttackInterval;
                if (float.IsPositiveInfinity(interval) || elapsedSinceAttack < interval)
                {
                    return;
                }

                elapsedSinceAttack -= interval;
                AttackOnce();
            }
        }

        private void Update()
        {
            if (autoAttack)
            {
                Tick(Time.deltaTime);
            }
        }

        private void EnsureConfigured()
        {
            if (weapon == null || target == null)
            {
                throw new InvalidOperationException(
                    "VerticalSliceCombatController requires both WeaponRuntimeBehaviour and CombatantBehaviour references. Call Configure or assign them in the Inspector.");
            }

            if (!weapon.IsInitialized)
            {
                throw new InvalidOperationException(
                    "The configured WeaponRuntimeBehaviour must be initialized before combat begins.");
            }
        }

        private static bool IsUsableSpeed(float speed)
        {
            return speed > 0f && !float.IsNaN(speed) && !float.IsInfinity(speed);
        }
    }
}
