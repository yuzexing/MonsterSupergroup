using System;
using System.Collections.Generic;
using MonsterSupergroup.GAS;
using MonsterSupergroup.GAS.Authoring;
using MonsterSupergroup.GAS.Unity;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Combat
{
    [DisallowMultipleComponent]
    public sealed class WeaponRuntimeBehaviour : MonoBehaviour, IWeaponRuntime
    {
        [SerializeField] private uint combatId = 1;
        [SerializeField] private AttackStats baseStats = new AttackStats
        {
            damage = 10,
            critMultiplier = 2f,
            speed = 1f,
            size = 1f,
            duration = 1f,
            projectileCount = 1
        };
        [SerializeField] private EquipmentModifierSet equipmentModifierSet = null;
        [SerializeField] private PerkModifierSet perkModifierSet = null;

        private RuntimeEquipmentModifiers runtimeModifiers;
        private AttackStatsMultipliers globalMultipliers;
        private CombatPipeline pipeline;

        public uint CombatId => combatId;

        public WeaponBehaviourStats Stats { get; private set; }

        public bool IsInitialized { get; private set; }

        public int ModifierCount => runtimeModifiers?.Count ?? 0;

        public AttackStatsMultipliers GlobalMultipliers => globalMultipliers;

        private void Awake()
        {
            if (!IsInitialized)
            {
                Initialize();
            }
        }

        public void Initialize(IRandomSource randomSource = null)
        {
            RuntimeModifierFactory factory = CreateFactory();
            var newModifiers = new RuntimeEquipmentModifiers();
            var newGlobalMultipliers = new AttackStatsMultipliers();

            try
            {
                ModifierSetRuntimeLoader.LoadEquipment(
                    equipmentModifierSet,
                    factory,
                    newModifiers);
                ModifierSetRuntimeLoader.ApplyWeaponStatPerks(
                    perkModifierSet,
                    factory,
                    newGlobalMultipliers);
                CommitInitialization(
                    baseStats,
                    combatId,
                    randomSource ?? new UnityRandomSource(),
                    newModifiers,
                    newGlobalMultipliers);
            }
            catch
            {
                newModifiers.Clear();
                throw;
            }
        }

        public void Initialize(
            AttackStats newBaseStats,
            IReadOnlyList<EquipmentDataModifier> equipment,
            IReadOnlyList<PerkDataModifier> perks,
            IRandomSource randomSource,
            uint newCombatId)
        {
            RuntimeModifierFactory factory = CreateFactory();
            var newModifiers = new RuntimeEquipmentModifiers();
            var newGlobalMultipliers = new AttackStatsMultipliers();

            try
            {
                ModifierSetRuntimeLoader.LoadEquipment(equipment, factory, newModifiers);
                ModifierSetRuntimeLoader.ApplyWeaponStatPerks(perks, factory, newGlobalMultipliers);
                CommitInitialization(
                    newBaseStats,
                    newCombatId,
                    randomSource ?? new UnityRandomSource(),
                    newModifiers,
                    newGlobalMultipliers);
            }
            catch
            {
                newModifiers.Clear();
                throw;
            }
        }

        public AttackSnapshot BeginAttack()
        {
            EnsureInitialized();
            return pipeline.BeginAttack(this, globalMultipliers);
        }

        public DamageInfo Attack(
            ICombatTarget target,
            float onHitChanceMultiplier = 1f,
            float onKillChanceMultiplier = 1f,
            float burnDamageMultiplier = 0f)
        {
            EnsureInitialized();
            AttackSnapshot attack = pipeline.BeginAttack(this, globalMultipliers);
            return pipeline.ResolveHit(
                attack,
                target,
                onHitChanceMultiplier,
                onKillChanceMultiplier,
                burnDamageMultiplier);
        }

        private static RuntimeModifierFactory CreateFactory()
        {
            return new RuntimeModifierFactory(GeneratedModifierRegistry.Create());
        }

        private void CommitInitialization(
            AttackStats newBaseStats,
            uint newCombatId,
            IRandomSource randomSource,
            RuntimeEquipmentModifiers newModifiers,
            AttackStatsMultipliers newGlobalMultipliers)
        {
            ReleaseRuntime();

            baseStats = newBaseStats;
            combatId = newCombatId;
            runtimeModifiers = newModifiers;
            globalMultipliers = newGlobalMultipliers;
            Stats = new WeaponBehaviourStats(newBaseStats, newGlobalMultipliers);
            pipeline = new CombatPipeline(newModifiers, randomSource);
            IsInitialized = true;

            try
            {
                // Prime derived stats once so consumers can read damage and speed before the first hit.
                pipeline.BeginAttack(this, globalMultipliers);
            }
            catch
            {
                ReleaseRuntime();
                throw;
            }
        }

        private void EnsureInitialized()
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException(
                    "WeaponRuntimeBehaviour is not initialized. Call Initialize before using it.");
            }
        }

        private void ReleaseRuntime()
        {
            if (runtimeModifiers != null)
            {
                runtimeModifiers.Clear();
            }

            runtimeModifiers = null;
            globalMultipliers = null;
            pipeline = null;
            Stats = null;
            IsInitialized = false;
        }

        public void Shutdown()
        {
            ReleaseRuntime();
        }

        private void OnDestroy()
        {
            Shutdown();
        }
    }
}
