using System;
using System.Collections.Generic;
using MonsterSupergroup.GAS;
using MonsterSupergroup.GAS.Authoring;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Combat
{
    public sealed class PlayerHandSlot : IDisposable
    {
        private readonly PlayerHand hand;
        private readonly Transform weaponParent;
        private readonly NearestEnemyTargetProvider targetProvider;
        private readonly CombatTeamBehaviour owner;
        private readonly IRandomSource randomSource;
        private readonly List<EquipmentModifierSet> equipment = new List<EquipmentModifierSet>();
        private CombatRuntimeServices runtimeServices;

        private ProjectileAttackBehaviour attackBehaviour;

        internal PlayerHandSlot(
            PlayerHand ownerHand,
            int index,
            Transform parent,
            NearestEnemyTargetProvider nearestTargetProvider,
            CombatTeamBehaviour weaponOwner,
            IRandomSource random,
            CombatRuntimeServices services = null)
        {
            hand = ownerHand ?? throw new ArgumentNullException(nameof(ownerHand));
            Index = index;
            weaponParent = parent ?? throw new ArgumentNullException(nameof(parent));
            targetProvider = nearestTargetProvider ?? throw new ArgumentNullException(nameof(nearestTargetProvider));
            owner = weaponOwner ?? throw new ArgumentNullException(nameof(weaponOwner));
            randomSource = random ?? throw new ArgumentNullException(nameof(random));
            runtimeServices = services;
        }

        public int Index { get; }
        public WeaponDefinition Definition { get; private set; }
        public WeaponRuntimeBehaviour Weapon => attackBehaviour != null ? attackBehaviour.Weapon : null;
        public ProjectileAttackBehaviour AttackBehaviour => attackBehaviour;
        public IReadOnlyList<EquipmentModifierSet> Equipment => equipment;
        public bool HasWeapon => attackBehaviour != null;

        public bool TryEquipWeapon(WeaponDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            definition.Validate();
            var candidateEquipment = new List<EquipmentModifierSet>(definition.StartingEquipment);
            ProjectileAttackBehaviour candidate = null;
            try
            {
                candidate = UnityEngine.Object.Instantiate(definition.WeaponPrefab, weaponParent);
                candidate.gameObject.SetActive(false);
                WeaponRuntimeBehaviour runtime = candidate.Weapon;
                runtime.InitializeOnAwake = false;
                runtimeServices?.Configure(runtime);
                runtime.Initialize(
                    definition.BaseStats,
                    FlattenEquipment(candidateEquipment),
                    definition.PerkModifierSet != null ? definition.PerkModifierSet.Modifiers : null,
                    randomSource,
                    definition.CombatId,
                    runtimeServices?.EventIds,
                    runtimeServices?.EventSink,
                    runtimeServices?.TriggerGuard,
                    runtimeServices?.TimeSource);
                candidate.Configure(runtime, definition, targetProvider, owner);
            }
            catch
            {
                if (candidate != null)
                {
                    candidate.Deactivate();
                    candidate.Weapon.Shutdown();
                    UnityEngine.Object.Destroy(candidate.gameObject);
                }

                throw;
            }

            ClearWeapon(false);
            Definition = definition;
            equipment.Clear();
            equipment.AddRange(candidateEquipment);
            attackBehaviour = candidate;
            if (hand.IsActive)
            {
                attackBehaviour.gameObject.SetActive(true);
                attackBehaviour.Activate();
            }

            hand.NotifySlotChanged(this);
            return true;
        }

        public bool TryAddEquipment(EquipmentModifierSet modifierSet)
        {
            if (modifierSet == null || !HasWeapon || equipment.Count >= PlayerHand.MaxEquipmentPerSlot ||
                equipment.Contains(modifierSet))
            {
                return false;
            }

            var candidate = new List<EquipmentModifierSet>(equipment) { modifierSet };
            Reinitialize(candidate);
            equipment.Add(modifierSet);
            hand.NotifySlotChanged(this);
            return true;
        }

        public bool TryRemoveEquipment(EquipmentModifierSet modifierSet)
        {
            int index = equipment.IndexOf(modifierSet);
            if (!HasWeapon || index < 0)
            {
                return false;
            }

            var candidate = new List<EquipmentModifierSet>(equipment);
            candidate.RemoveAt(index);
            Reinitialize(candidate);
            equipment.RemoveAt(index);
            hand.NotifySlotChanged(this);
            return true;
        }

        public void ActivateWeapon()
        {
            if (!HasWeapon)
            {
                return;
            }

            attackBehaviour.gameObject.SetActive(true);
            attackBehaviour.Activate();
        }

        public void DeactivateWeapon()
        {
            if (!HasWeapon)
            {
                return;
            }

            attackBehaviour.Deactivate();
            attackBehaviour.gameObject.SetActive(false);
        }

        public bool ClearWeapon(bool notify = true)
        {
            if (!HasWeapon)
            {
                return false;
            }

            ProjectileAttackBehaviour previous = attackBehaviour;
            attackBehaviour = null;
            Definition = null;
            equipment.Clear();
            previous.Deactivate();
            previous.Weapon.Shutdown();
            UnityEngine.Object.Destroy(previous.gameObject);
            if (notify)
            {
                hand.NotifySlotChanged(this);
            }

            return true;
        }

        private void Reinitialize(IReadOnlyList<EquipmentModifierSet> candidateEquipment)
        {
            runtimeServices?.Configure(Weapon);
            Weapon.Initialize(
                Definition.BaseStats,
                FlattenEquipment(candidateEquipment),
                Definition.PerkModifierSet != null ? Definition.PerkModifierSet.Modifiers : null,
                randomSource,
                Definition.CombatId,
                runtimeServices?.EventIds,
                runtimeServices?.EventSink,
                runtimeServices?.TriggerGuard,
                runtimeServices?.TimeSource);
        }

        public void ConfigureCombatRuntimeServices(CombatRuntimeServices services)
        {
            runtimeServices = services ?? throw new ArgumentNullException(nameof(services));
            if (HasWeapon)
            {
                Reinitialize(equipment);
            }
        }

        private static IReadOnlyList<EquipmentDataModifier> FlattenEquipment(
            IReadOnlyList<EquipmentModifierSet> equipmentSets)
        {
            var result = new List<EquipmentDataModifier>();
            for (int i = 0; i < equipmentSets.Count; i++)
            {
                EquipmentModifierSet set = equipmentSets[i];
                if (set == null)
                {
                    throw new InvalidOperationException($"Equipment entry {i} is null.");
                }

                result.AddRange(set.Modifiers);
            }

            return result;
        }

        public void Dispose()
        {
            ClearWeapon(false);
        }
    }
}
