using System;
using System.Collections.Generic;
using MonsterSupergroup.GAS;
using MonsterSupergroup.GAS.Authoring;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Combat
{
    [CreateAssetMenu(
        fileName = "ProjectileWeapon",
        menuName = "Monster Supergroup/Gameplay/Projectile Weapon")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        [SerializeField] private uint combatId = 1;
        [SerializeField] private AttackStats baseStats = new AttackStats
        {
            damage = 10,
            critMultiplier = 2f,
            speed = 1f,
            size = 1f,
            duration = 5f,
            projectileCount = 1
        };
        [SerializeField] private ProjectileAttackBehaviour weaponPrefab;
        [SerializeField] private StraightProjectileBehaviour projectilePrefab;
        [SerializeField] private List<EquipmentModifierSet> startingEquipment = new List<EquipmentModifierSet>();
        [SerializeField] private PerkModifierSet perkModifierSet;
        [SerializeField, Min(0.1f)] private float targetRange = 20f;
        [SerializeField, Min(0.1f)] private float projectileSpeed = 10f;
        [SerializeField, Min(1)] private int projectileHitCount = 1;
        [SerializeField, Min(0f)] private float spawnRadius = 0.5f;
        [SerializeField] private Vector3 spawnOffset;
        [SerializeField] private bool rotateToMovement = true;

        public uint CombatId => combatId;
        public AttackStats BaseStats => baseStats;
        public ProjectileAttackBehaviour WeaponPrefab => weaponPrefab;
        public StraightProjectileBehaviour ProjectilePrefab => projectilePrefab;
        public IReadOnlyList<EquipmentModifierSet> StartingEquipment => startingEquipment;
        public PerkModifierSet PerkModifierSet => perkModifierSet;
        public float TargetRange => targetRange;
        public float ProjectileSpeed => projectileSpeed;
        public int ProjectileHitCount => projectileHitCount;
        public float SpawnRadius => spawnRadius;
        public Vector3 SpawnOffset => spawnOffset;
        public bool RotateToMovement => rotateToMovement;

        public void Configure(
            uint newCombatId,
            AttackStats newBaseStats,
            ProjectileAttackBehaviour newWeaponPrefab,
            StraightProjectileBehaviour newProjectilePrefab,
            IReadOnlyList<EquipmentModifierSet> newStartingEquipment = null,
            PerkModifierSet newPerkModifierSet = null,
            float newTargetRange = 20f,
            float newProjectileSpeed = 10f,
            int newProjectileHitCount = 1,
            float newSpawnRadius = 0.5f,
            Vector3 newSpawnOffset = default,
            bool newRotateToMovement = true)
        {
            if (newCombatId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(newCombatId));
            }

            if (newWeaponPrefab == null)
            {
                throw new ArgumentNullException(nameof(newWeaponPrefab));
            }

            if (newProjectilePrefab == null)
            {
                throw new ArgumentNullException(nameof(newProjectilePrefab));
            }

            if (!IsFinitePositive(newTargetRange))
            {
                throw new ArgumentOutOfRangeException(nameof(newTargetRange));
            }

            if (!IsFinitePositive(newProjectileSpeed))
            {
                throw new ArgumentOutOfRangeException(nameof(newProjectileSpeed));
            }

            if (newProjectileHitCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(newProjectileHitCount));
            }

            if (!IsFiniteNonNegative(newSpawnRadius))
            {
                throw new ArgumentOutOfRangeException(nameof(newSpawnRadius));
            }

            if (newStartingEquipment != null && newStartingEquipment.Count > PlayerHand.MaxEquipmentPerSlot)
            {
                throw new ArgumentException(
                    $"A weapon cannot start with more than {PlayerHand.MaxEquipmentPerSlot} equipment sets.",
                    nameof(newStartingEquipment));
            }

            combatId = newCombatId;
            baseStats = newBaseStats;
            weaponPrefab = newWeaponPrefab;
            projectilePrefab = newProjectilePrefab;
            startingEquipment = newStartingEquipment == null
                ? new List<EquipmentModifierSet>()
                : new List<EquipmentModifierSet>(newStartingEquipment);
            perkModifierSet = newPerkModifierSet;
            targetRange = newTargetRange;
            projectileSpeed = newProjectileSpeed;
            projectileHitCount = newProjectileHitCount;
            spawnRadius = newSpawnRadius;
            spawnOffset = newSpawnOffset;
            rotateToMovement = newRotateToMovement;
        }

        public void Validate()
        {
            if (combatId == 0)
            {
                throw new InvalidOperationException($"{name} has an invalid zero Combat ID.");
            }

            if (weaponPrefab == null || projectilePrefab == null)
            {
                throw new InvalidOperationException($"{name} is missing its weapon or projectile prefab.");
            }

            if (startingEquipment == null || startingEquipment.Count > PlayerHand.MaxEquipmentPerSlot)
            {
                throw new InvalidOperationException(
                    $"{name} must contain zero to {PlayerHand.MaxEquipmentPerSlot} starting equipment sets.");
            }

            for (int i = 0; i < startingEquipment.Count; i++)
            {
                if (startingEquipment[i] == null)
                {
                    throw new InvalidOperationException($"{name} has a null starting equipment entry at index {i}.");
                }
            }

            if (!IsFinitePositive(targetRange) || !IsFinitePositive(projectileSpeed) ||
                projectileHitCount < 1 || !IsFiniteNonNegative(spawnRadius))
            {
                throw new InvalidOperationException($"{name} contains invalid projectile settings.");
            }
        }

        private static bool IsFinitePositive(float value)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFiniteNonNegative(float value)
        {
            return value >= 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
