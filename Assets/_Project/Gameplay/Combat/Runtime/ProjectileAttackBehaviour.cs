using System;
using System.Collections.Generic;
using MonsterSupergroup.GAS;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Combat
{
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WeaponRuntimeBehaviour))]
    public sealed class ProjectileAttackBehaviour : MonoBehaviour
    {
        private readonly List<StraightProjectileBehaviour> activeProjectiles =
            new List<StraightProjectileBehaviour>();

        private WeaponRuntimeBehaviour weapon;
        private WeaponDefinition definition;
        private NearestEnemyTargetProvider targetProvider;
        private CombatTeamBehaviour owner;
        private float elapsed;
        private bool configured;
        private bool attackEnabled;

        public WeaponRuntimeBehaviour Weapon => weapon != null ? weapon : GetComponent<WeaponRuntimeBehaviour>();
        public int ActiveProjectileCount => activeProjectiles.Count;
        public bool IsConfigured => configured;

        public void Configure(
            WeaponRuntimeBehaviour sourceWeapon,
            WeaponDefinition weaponDefinition,
            NearestEnemyTargetProvider nearestTargetProvider,
            CombatTeamBehaviour weaponOwner)
        {
            weapon = sourceWeapon ?? throw new ArgumentNullException(nameof(sourceWeapon));
            definition = weaponDefinition ?? throw new ArgumentNullException(nameof(weaponDefinition));
            targetProvider = nearestTargetProvider ?? throw new ArgumentNullException(nameof(nearestTargetProvider));
            owner = weaponOwner ?? throw new ArgumentNullException(nameof(weaponOwner));
            definition.Validate();
            if (!weapon.IsInitialized)
            {
                throw new InvalidOperationException("The weapon runtime must be initialized before configuring attacks.");
            }

            elapsed = float.PositiveInfinity;
            configured = true;
        }

        public void Activate()
        {
            EnsureConfigured();
            attackEnabled = true;
            elapsed = float.PositiveInfinity;
            enabled = true;
        }

        public void Deactivate()
        {
            attackEnabled = false;
            enabled = false;
            CancelProjectiles();
            elapsed = 0f;
        }

        public bool TryAttack()
        {
            EnsureConfigured();
            Vector2 origin = transform.position;
            if (!targetProvider.TryGetNearest(origin, definition.TargetRange, out _, out Vector2 direction))
            {
                return false;
            }

            FireVolley(direction);
            return true;
        }

        private void Update()
        {
            if (!configured || !attackEnabled)
            {
                return;
            }

            float speed = weapon.Stats.SpeedValue;
            if (speed <= 0f || float.IsNaN(speed) || float.IsInfinity(speed))
            {
                return;
            }

            float interval = 1f / speed;
            elapsed += Time.deltaTime;
            if (elapsed >= interval && TryAttack())
            {
                elapsed = 0f;
            }
        }

        private void FireVolley(Vector2 targetDirection)
        {
            AttackSnapshot attack = weapon.BeginAttack();
            int projectileCount = Mathf.Max(0, attack.Stats.ProjectileCount);
            if (projectileCount == 0)
            {
                return;
            }

            for (int i = 0; i < projectileCount; i++)
            {
                Vector2 direction = projectileCount == 1
                    ? targetDirection.normalized
                    : Quaternion.Euler(0f, 0f, 360f * i / projectileCount) * targetDirection.normalized;
                Vector3 spawnPosition = transform.position + definition.SpawnOffset +
                    (Vector3)(direction * definition.SpawnRadius);
                StraightProjectileBehaviour projectile = StraightProjectilePool.Spawn(
                    definition.ProjectilePrefab,
                    spawnPosition,
                    Quaternion.identity);
                activeProjectiles.Add(projectile);
                projectile.Initialize(
                    weapon,
                    attack,
                    owner.Team,
                    direction,
                    definition.ProjectileSpeed,
                    definition.ProjectileHitCount,
                    definition.RotateToMovement,
                    HandleProjectileFinished);
            }
        }

        private void HandleProjectileFinished(StraightProjectileBehaviour projectile)
        {
            activeProjectiles.Remove(projectile);
            StraightProjectilePool.Release(projectile);
        }

        private void CancelProjectiles()
        {
            StraightProjectileBehaviour[] projectiles = activeProjectiles.ToArray();
            activeProjectiles.Clear();
            for (int i = 0; i < projectiles.Length; i++)
            {
                if (projectiles[i] != null)
                {
                    projectiles[i].Cancel();
                }
            }
        }

        private void EnsureConfigured()
        {
            if (!configured || weapon == null || definition == null || targetProvider == null || owner == null)
            {
                throw new InvalidOperationException("ProjectileAttackBehaviour is not configured.");
            }
        }

        private void OnDestroy()
        {
            CancelProjectiles();
            configured = false;
        }
    }
}
