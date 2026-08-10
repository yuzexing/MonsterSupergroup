using System;
using System.Collections.Generic;
using MonsterSupergroup.GAS.Authoring;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.GAS.Editor
{
    public enum GasValidationSeverity
    {
        Warning,
        Error
    }

    public readonly struct GasValidationIssue
    {
        public GasValidationIssue(GasValidationSeverity severity, UnityEngine.Object context, string message)
        {
            Severity = severity;
            Context = context;
            Message = message;
        }

        public GasValidationSeverity Severity { get; }
        public UnityEngine.Object Context { get; }
        public string Message { get; }
    }

    public static class GasAssetValidator
    {
        [MenuItem("Tools/MonsterSupergroup/GAS/Validate All")]
        public static void ValidateAllMenu()
        {
            IReadOnlyList<GasValidationIssue> issues = ValidateAllAssets();
            foreach (GasValidationIssue issue in issues)
            {
                if (issue.Severity == GasValidationSeverity.Error)
                {
                    Debug.LogError(issue.Message, issue.Context);
                }
                else
                {
                    Debug.LogWarning(issue.Message, issue.Context);
                }
            }

            if (issues.Count == 0)
            {
                Debug.Log("All GAS assets and the generated registry are valid.");
            }
        }

        public static IReadOnlyList<GasValidationIssue> ValidateAllAssets()
        {
            var issues = new List<GasValidationIssue>();
            ValidateCatalog(ModifierTypeCatalog.Equipment, "equipment", issues);
            ValidateCatalog(ModifierTypeCatalog.Perks, "perk", issues);

            foreach (string guid in AssetDatabase.FindAssets("t:EquipmentModifierSet"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                EquipmentModifierSet set = AssetDatabase.LoadAssetAtPath<EquipmentModifierSet>(path);
                Validate(set, issues);
            }

            foreach (string guid in AssetDatabase.FindAssets("t:PerkModifierSet"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                PerkModifierSet set = AssetDatabase.LoadAssetAtPath<PerkModifierSet>(path);
                Validate(set, issues);
            }

            if (!ModifierRegistryGenerator.IsCurrent())
            {
                issues.Add(new GasValidationIssue(
                    GasValidationSeverity.Error,
                    null,
                    $"Generated GAS registry is missing or stale. Run Tools/MonsterSupergroup/GAS/Rebuild Registry ({ModifierRegistryGenerator.OutputPath})."));
            }

            return issues;
        }

        public static IReadOnlyList<GasValidationIssue> Validate(EquipmentModifierSet set)
        {
            var issues = new List<GasValidationIssue>();
            Validate(set, issues);
            return issues;
        }

        public static IReadOnlyList<GasValidationIssue> Validate(PerkModifierSet set)
        {
            var issues = new List<GasValidationIssue>();
            Validate(set, issues);
            return issues;
        }

        private static void Validate(EquipmentModifierSet set, ICollection<GasValidationIssue> issues)
        {
            if (set == null)
            {
                return;
            }

            for (int index = 0; index < set.Modifiers.Count; index++)
            {
                EquipmentDataModifier data = set.Modifiers[index];
                if (data == null)
                {
                    AddError(issues, set, $"{set.name}: equipment modifier {index} is null.");
                    continue;
                }

                ValidateData(
                    data.ModifierIdValue,
                    data.Parameters,
                    ModifierTypeCatalog.TryGetEquipment,
                    set,
                    $"{set.name}: equipment modifier {index}",
                    issues);
            }
        }

        private static void Validate(PerkModifierSet set, ICollection<GasValidationIssue> issues)
        {
            if (set == null)
            {
                return;
            }

            for (int index = 0; index < set.Modifiers.Count; index++)
            {
                PerkDataModifier data = set.Modifiers[index];
                if (data == null)
                {
                    AddError(issues, set, $"{set.name}: perk modifier {index} is null.");
                    continue;
                }

                ValidateData(
                    data.ModifierIdValue,
                    data.Parameters,
                    ModifierTypeCatalog.TryGetPerk,
                    set,
                    $"{set.name}: perk modifier {index}",
                    issues);
            }
        }

        private static void ValidateData(
            uint id,
            object parameters,
            TryGetDescriptor tryGetDescriptor,
            UnityEngine.Object context,
            string label,
            ICollection<GasValidationIssue> issues)
        {
            if (id == 0)
            {
                AddError(issues, context, $"{label} has invalid ID 0.");
                return;
            }

            if (!tryGetDescriptor(id, out ModifierDescriptor descriptor))
            {
                AddError(issues, context, $"{label} uses unknown modifier ID {id}.");
                return;
            }

            if (parameters == null)
            {
                AddError(issues, context, $"{label} has null parameters.");
                return;
            }

            Type parametersType = parameters.GetType();
            if (parametersType != descriptor.ParametersType)
            {
                AddError(
                    issues,
                    context,
                    $"{label} expects {descriptor.ParametersType.FullName}, but contains {parametersType.FullName}.");
                return;
            }

            if (!parametersType.IsSerializable)
            {
                AddError(issues, context, $"{label} parameters type {parametersType.FullName} is not [Serializable].");
            }

            switch (parameters)
            {
                case DamageStatModifierParameters damage:
                    ValidateFiniteIncrement(
                        damage.MultiplierIncrement,
                        "Damage multiplier increment",
                        context,
                        label,
                        issues);
                    break;
                case OnHitBurnModifierParameters burn:
                    ValidateBurnParameters(burn, context, label, issues);
                    break;
                case WeaponSpeedPerkModifierParameters speed:
                    ValidateFiniteIncrement(
                        speed.MultiplierIncrement,
                        "Weapon speed multiplier increment",
                        context,
                        label,
                        issues);
                    break;
            }
        }

        private static void ValidateBurnParameters(
            OnHitBurnModifierParameters parameters,
            UnityEngine.Object context,
            string label,
            ICollection<GasValidationIssue> issues)
        {
            float chance = parameters.Chance;
            float damageMultiplier = parameters.DamageMultiplier;
            int numberOfHits = parameters.NumberOfHits;
            float interval = parameters.HitIntervalDuration;

            if (!IsFinite(chance) || chance < 0f || chance > 1f)
            {
                AddError(issues, context, $"{label} Burn chance must be in [0, 1].");
            }

            if (!IsFinite(damageMultiplier) || damageMultiplier < 0f)
            {
                AddError(issues, context, $"{label} Burn damage multiplier must be finite and non-negative.");
            }

            if (numberOfHits < 1)
            {
                AddError(issues, context, $"{label} Burn number of hits must be at least one.");
            }

            if (!IsFinite(interval) || interval <= 0f)
            {
                AddError(issues, context, $"{label} Burn hit interval must be finite and greater than zero.");
            }
        }

        private static void ValidateFiniteIncrement(
            float value,
            string valueName,
            UnityEngine.Object context,
            string label,
            ICollection<GasValidationIssue> issues)
        {
            if (!IsFinite(value))
            {
                AddError(issues, context, $"{label} {valueName} must be finite.");
            }
        }

        private static void ValidateCatalog(
            IReadOnlyList<ModifierDescriptor> descriptors,
            string domain,
            ICollection<GasValidationIssue> issues)
        {
            var ids = new HashSet<uint>();
            var types = new HashSet<Type>();
            foreach (ModifierDescriptor descriptor in descriptors)
            {
                if (descriptor.Id == 0)
                {
                    AddError(issues, null, $"{descriptor.ModifierType.FullName} has invalid {domain} modifier ID 0.");
                }

                if (!ids.Add(descriptor.Id))
                {
                    AddError(issues, null, $"Duplicate {domain} modifier ID {descriptor.Id}.");
                }

                if (!types.Add(descriptor.ModifierType))
                {
                    AddError(issues, null, $"Duplicate {domain} modifier type {descriptor.ModifierType.FullName}.");
                }

                if (!descriptor.ParametersType.IsSerializable)
                {
                    AddError(issues, null, $"{descriptor.ParametersType.FullName} must be [Serializable].");
                }
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void AddError(
            ICollection<GasValidationIssue> issues,
            UnityEngine.Object context,
            string message)
        {
            issues.Add(new GasValidationIssue(GasValidationSeverity.Error, context, message));
        }

        private delegate bool TryGetDescriptor(uint id, out ModifierDescriptor descriptor);
    }
}
