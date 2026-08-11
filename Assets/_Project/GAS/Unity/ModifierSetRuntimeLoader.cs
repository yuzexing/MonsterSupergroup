using System;
using System.Collections.Generic;
using MonsterSupergroup.GAS.Authoring;

namespace MonsterSupergroup.GAS.Unity
{
    public static class ModifierSetRuntimeLoader
    {
        public static void LoadEquipment(
            EquipmentModifierSet modifierSet,
            RuntimeModifierFactory factory,
            RuntimeEquipmentModifiers destination)
        {
            LoadEquipment(modifierSet == null ? null : modifierSet.Modifiers, factory, destination);
        }

        public static void LoadEquipment(
            IReadOnlyList<EquipmentDataModifier> modifiers,
            RuntimeModifierFactory factory,
            RuntimeEquipmentModifiers destination)
        {
            EnsureEquipmentDependencies(factory, destination);
            if (modifiers == null)
            {
                return;
            }

            var addedHandles = new List<ModifierHandle>(modifiers.Count);
            try
            {
                for (int i = 0; i < modifiers.Count; i++)
                {
                    EquipmentDataModifier data = modifiers[i];
                    if (data == null)
                    {
                        throw new ArgumentException($"Equipment modifier at index {i} is null.", nameof(modifiers));
                    }

                    RuntimeEquipmentModifier runtimeModifier = data.CreateRuntime(factory);
                    try
                    {
                        addedHandles.Add(destination.Add(runtimeModifier));
                    }
                    catch
                    {
                        runtimeModifier.Dispose();
                        throw;
                    }
                }
            }
            catch
            {
                for (int i = addedHandles.Count - 1; i >= 0; i--)
                {
                    destination.Remove(addedHandles[i]);
                }

                throw;
            }
        }

        public static void ApplyWeaponStatPerks(
            PerkModifierSet modifierSet,
            RuntimeModifierFactory factory,
            AttackStatsMultipliers multipliers)
        {
            ApplyWeaponStatPerks(modifierSet == null ? null : modifierSet.Modifiers, factory, multipliers);
        }

        public static void ApplyWeaponStatPerks(
            IReadOnlyList<PerkDataModifier> modifiers,
            RuntimeModifierFactory factory,
            AttackStatsMultipliers multipliers)
        {
            EnsurePerkDependencies(factory, multipliers);
            if (modifiers == null)
            {
                return;
            }

            AttackStatsMultipliers snapshot = multipliers.Clone();
            try
            {
                for (int i = 0; i < modifiers.Count; i++)
                {
                    PerkDataModifier data = modifiers[i];
                    if (data == null)
                    {
                        throw new ArgumentException($"Perk modifier at index {i} is null.", nameof(modifiers));
                    }

                    RuntimePerkModifier runtimeModifier = data.CreateRuntime(factory);
                    if (!(runtimeModifier is WeaponStatsPerkModifier weaponStatsModifier))
                    {
                        throw new NotSupportedException(
                            $"Perk modifier type {runtimeModifier.GetType().FullName} does not support the weapon stats stage.");
                    }

                    weaponStatsModifier.Apply(multipliers);
                }
            }
            catch
            {
                multipliers.CopyFrom(snapshot);
                throw;
            }
        }

        private static void EnsureEquipmentDependencies(
            RuntimeModifierFactory factory,
            RuntimeEquipmentModifiers destination)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
        }

        private static void EnsurePerkDependencies(
            RuntimeModifierFactory factory,
            AttackStatsMultipliers multipliers)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            if (multipliers == null)
            {
                throw new ArgumentNullException(nameof(multipliers));
            }
        }
    }
}
