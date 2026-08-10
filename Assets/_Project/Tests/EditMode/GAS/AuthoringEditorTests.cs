using System;
using System.Linq;
using MonsterSupergroup.GAS.Authoring;
using MonsterSupergroup.GAS.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.GAS.Tests
{
    public sealed class AuthoringEditorTests
    {
        [SetUp]
        public void SetUp()
        {
            ModifierTypeCatalog.Refresh();
        }

        [Test]
        public void Catalog_DiscoversFirstSliceTypesInStableIdOrder()
        {
            Assert.That(ModifierTypeCatalog.Equipment.Select(item => item.Id), Is.Ordered);
            Assert.That(ModifierTypeCatalog.Perks.Select(item => item.Id), Is.Ordered);
            Assert.That(
                ModifierTypeCatalog.Equipment.Any(item =>
                    item.Id == DamageStatModifier.ModifierIdValue &&
                    item.ParametersType == typeof(DamageStatModifierParameters)),
                Is.True);
            Assert.That(
                ModifierTypeCatalog.Equipment.Any(item =>
                    item.Id == OnHitBurnModifier.ModifierIdValue &&
                    item.ParametersType == typeof(OnHitBurnModifierParameters)),
                Is.True);
            Assert.That(
                ModifierTypeCatalog.Perks.Any(item =>
                    item.Id == WeaponSpeedPerkModifier.ModifierIdValue &&
                    item.ParametersType == typeof(WeaponSpeedPerkModifierParameters)),
                Is.True);
        }

        [Test]
        public void SelectionService_SetsStableIdAndCreatesExactParametersType()
        {
            EquipmentModifierSet set = CreateEquipmentSet(out SerializedObject serialized, out SerializedProperty entry);
            try
            {
                Assert.That(
                    ModifierTypeCatalog.TryGetEquipment(DamageStatModifier.ModifierIdValue, out ModifierDescriptor descriptor),
                    Is.True);

                ModifierSelectionService.Assign(entry, descriptor);

                Assert.That(set.Modifiers[0].ModifierIdValue, Is.EqualTo(DamageStatModifier.ModifierIdValue));
                Assert.That(set.Modifiers[0].Parameters, Is.TypeOf<DamageStatModifierParameters>());
                Assert.That(
                    set.Modifiers[0].CreateRuntime(new RuntimeModifierFactory(GeneratedModifierRegistry.Create())),
                    Is.TypeOf<DamageStatModifier>());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(set);
                serialized.Dispose();
            }
        }

        [Test]
        public void SelectionService_ReportsWhenChangingTypeWouldReplaceParameters()
        {
            EquipmentModifierSet set = CreateEquipmentSet(out SerializedObject serialized, out SerializedProperty entry);
            try
            {
                ModifierTypeCatalog.TryGetEquipment(DamageStatModifier.ModifierIdValue, out ModifierDescriptor damage);
                ModifierTypeCatalog.TryGetEquipment(OnHitBurnModifier.ModifierIdValue, out ModifierDescriptor burn);
                ModifierSelectionService.Assign(entry, damage);
                serialized.Update();
                entry = serialized.FindProperty("modifiers").GetArrayElementAtIndex(0);

                Assert.That(ModifierSelectionService.RequiresParameterReplacement(entry, burn), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(set);
                serialized.Dispose();
            }
        }

        [Test]
        public void PerkAuthoring_CreatesRegisteredRuntimePerk()
        {
            PerkModifierSet set = ScriptableObject.CreateInstance<PerkModifierSet>();
            var serialized = new SerializedObject(set);
            try
            {
                SerializedProperty modifiers = serialized.FindProperty("modifiers");
                modifiers.arraySize = 1;
                serialized.ApplyModifiedProperties();
                serialized.Update();
                SerializedProperty entry = serialized.FindProperty("modifiers").GetArrayElementAtIndex(0);
                ModifierTypeCatalog.TryGetPerk(
                    WeaponSpeedPerkModifier.ModifierIdValue,
                    out ModifierDescriptor descriptor);

                ModifierSelectionService.Assign(entry, descriptor);

                Assert.That(set.Modifiers[0].Parameters, Is.TypeOf<WeaponSpeedPerkModifierParameters>());
                Assert.That(
                    set.Modifiers[0].CreateRuntime(new RuntimeModifierFactory(GeneratedModifierRegistry.Create())),
                    Is.TypeOf<WeaponSpeedPerkModifier>());
                Assert.That(GasAssetValidator.Validate(set), Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(set);
                serialized.Dispose();
            }
        }

        [Test]
        public void SerializeReference_RoundTripsConcreteParameters()
        {
            EquipmentModifierSet original = CreateEquipmentSet(out SerializedObject serialized, out SerializedProperty entry);
            EquipmentModifierSet clone = ScriptableObject.CreateInstance<EquipmentModifierSet>();
            try
            {
                ModifierTypeCatalog.TryGetEquipment(OnHitBurnModifier.ModifierIdValue, out ModifierDescriptor burn);
                ModifierSelectionService.Assign(entry, burn);
                var parameters = (OnHitBurnModifierParameters)original.Modifiers[0].Parameters;
                parameters.chance = 0.75f;
                parameters.damageMultiplier = 0.5f;
                parameters.numberOfHits = 3;
                parameters.hitIntervalDuration = 0.25f;

                string json = EditorJsonUtility.ToJson(original);
                EditorJsonUtility.FromJsonOverwrite(json, clone);

                Assert.That(clone.Modifiers[0].ModifierIdValue, Is.EqualTo(OnHitBurnModifier.ModifierIdValue));
                Assert.That(clone.Modifiers[0].Parameters, Is.TypeOf<OnHitBurnModifierParameters>());
                Assert.That(
                    ((OnHitBurnModifierParameters)clone.Modifiers[0].Parameters).numberOfHits,
                    Is.EqualTo(3));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(original);
                UnityEngine.Object.DestroyImmediate(clone);
                serialized.Dispose();
            }
        }

        [Test]
        public void Validator_CatchesZeroUnknownNullMismatchAndBurnBounds()
        {
            EquipmentModifierSet set = CreateEquipmentSet(out SerializedObject serialized, out SerializedProperty entry);
            try
            {
                Assert.That(GasAssetValidator.Validate(set).Any(issue => issue.Message.Contains("ID 0")), Is.True);

                SetRaw(entry, 0xFFFFFFFFu, null);
                Assert.That(GasAssetValidator.Validate(set).Any(issue => issue.Message.Contains("unknown")), Is.True);

                SetRaw(entry, DamageStatModifier.ModifierIdValue, null);
                Assert.That(GasAssetValidator.Validate(set).Any(issue => issue.Message.Contains("null parameters")), Is.True);

                SetRaw(entry, DamageStatModifier.ModifierIdValue, new OnHitBurnModifierParameters(1f, 1f, 1, 1f));
                Assert.That(GasAssetValidator.Validate(set).Any(issue => issue.Message.Contains("expects")), Is.True);

                SetRaw(entry, OnHitBurnModifier.ModifierIdValue, new OnHitBurnModifierParameters(2f, -1f, 0, 0f));
                var messages = GasAssetValidator.Validate(set).Select(issue => issue.Message).ToArray();
                Assert.That(messages.Any(message => message.Contains("chance")), Is.True);
                Assert.That(messages.Any(message => message.Contains("damage multiplier")), Is.True);
                Assert.That(messages.Any(message => message.Contains("number of hits")), Is.True);
                Assert.That(messages.Any(message => message.Contains("hit interval")), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(set);
                serialized.Dispose();
            }
        }

        [Test]
        public void Validator_RejectsNonFiniteConcreteMultiplierParameters()
        {
            EquipmentModifierSet equipment = CreateEquipmentSet(
                out SerializedObject equipmentSerialized,
                out SerializedProperty equipmentEntry);
            PerkModifierSet perks = ScriptableObject.CreateInstance<PerkModifierSet>();
            var perkSerialized = new SerializedObject(perks);
            try
            {
                SetRaw(
                    equipmentEntry,
                    DamageStatModifier.ModifierIdValue,
                    new DamageStatModifierParameters { multiplierIncrement = float.NaN });

                SerializedProperty perkList = perkSerialized.FindProperty("modifiers");
                perkList.arraySize = 1;
                perkSerialized.ApplyModifiedProperties();
                perkSerialized.Update();
                SerializedProperty perkEntry = perkSerialized
                    .FindProperty("modifiers")
                    .GetArrayElementAtIndex(0);
                SetRawPerk(
                    perkEntry,
                    WeaponSpeedPerkModifier.ModifierIdValue,
                    new WeaponSpeedPerkModifierParameters
                    {
                        multiplierIncrement = float.PositiveInfinity
                    });

                Assert.That(
                    GasAssetValidator.Validate(equipment).Any(issue => issue.Message.Contains("finite")),
                    Is.True);
                Assert.That(
                    GasAssetValidator.Validate(perks).Any(issue => issue.Message.Contains("finite")),
                    Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(equipment);
                UnityEngine.Object.DestroyImmediate(perks);
                equipmentSerialized.Dispose();
                perkSerialized.Dispose();
            }
        }

        [Test]
        public void Validator_AllowsRepeatedModifierIdsWithinOneAsset()
        {
            EquipmentModifierSet set = ScriptableObject.CreateInstance<EquipmentModifierSet>();
            var serialized = new SerializedObject(set);
            try
            {
                SerializedProperty modifiers = serialized.FindProperty("modifiers");
                modifiers.arraySize = 2;
                serialized.ApplyModifiedProperties();
                serialized.Update();
                ModifierTypeCatalog.TryGetEquipment(
                    DamageStatModifier.ModifierIdValue,
                    out ModifierDescriptor damage);

                for (int index = 0; index < 2; index++)
                {
                    SerializedProperty entry = serialized
                        .FindProperty("modifiers")
                        .GetArrayElementAtIndex(index);
                    ModifierSelectionService.Assign(entry, damage);
                    serialized.Update();
                }

                Assert.That(set.Modifiers, Has.Count.EqualTo(2));
                Assert.That(set.Modifiers[0].ModifierId, Is.EqualTo(set.Modifiers[1].ModifierId));
                Assert.That(GasAssetValidator.Validate(set), Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(set);
                serialized.Dispose();
            }
        }

        [Test]
        public void RegistryGenerator_IsDeterministicCurrentAndContainsNoRuntimeReflection()
        {
            string first = ModifierRegistryGenerator.GenerateSource();
            string second = ModifierRegistryGenerator.GenerateSource();

            Assert.That(second, Is.EqualTo(first));
            Assert.That(ModifierRegistryGenerator.IsCurrent(), Is.True);
            Assert.That(first, Does.Not.Contain("Activator.CreateInstance"));
            Assert.That(first, Does.Not.Contain("AppDomain.CurrentDomain"));
            Assert.That(
                first.IndexOf("DamageStatModifier", StringComparison.Ordinal),
                Is.LessThan(first.IndexOf("OnHitBurnModifier", StringComparison.Ordinal)));
        }

        private static EquipmentModifierSet CreateEquipmentSet(
            out SerializedObject serialized,
            out SerializedProperty entry)
        {
            EquipmentModifierSet set = ScriptableObject.CreateInstance<EquipmentModifierSet>();
            serialized = new SerializedObject(set);
            SerializedProperty modifiers = serialized.FindProperty("modifiers");
            modifiers.arraySize = 1;
            serialized.ApplyModifiedProperties();
            serialized.Update();
            entry = serialized.FindProperty("modifiers").GetArrayElementAtIndex(0);
            return set;
        }

        private static void SetRaw(SerializedProperty entry, uint id, EquipmentModifierParameters parameters)
        {
            entry.FindPropertyRelative(ModifierSelectionService.ModifierIdFieldName).uintValue = id;
            entry.FindPropertyRelative(ModifierSelectionService.ParametersFieldName).managedReferenceValue = parameters;
            entry.serializedObject.ApplyModifiedProperties();
            entry.serializedObject.Update();
        }

        private static void SetRawPerk(SerializedProperty entry, uint id, PerkModifierParameters parameters)
        {
            entry.FindPropertyRelative(ModifierSelectionService.ModifierIdFieldName).uintValue = id;
            entry.FindPropertyRelative(ModifierSelectionService.ParametersFieldName).managedReferenceValue = parameters;
            entry.serializedObject.ApplyModifiedProperties();
            entry.serializedObject.Update();
        }
    }
}
