using System.Collections.Generic;
using AstralShift.HellMaiden.Data.Cards;
using MonsterSupergroup.GAS;
using MonsterSupergroup.GAS.Authoring;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.HellMaidenMigration.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.HellMaidenMigration.Tests
{
    public sealed class DanteNativeGasMigrationTests
    {
        private static readonly string[] NativeEquipmentPaths =
        {
            DanteNativeGasMigration.OutputFolder + "/NativeGasEquipment_Damage.asset",
            DanteNativeGasMigration.OutputFolder + "/NativeGasEquipment_Speed.asset",
            DanteNativeGasMigration.OutputFolder + "/NativeGasEquipment_Size.asset",
            DanteNativeGasMigration.OutputFolder + "/NativeGasEquipment_Duration.asset",
            DanteNativeGasMigration.OutputFolder + "/NativeGasEquipment_CritRate.asset",
            DanteNativeGasMigration.OutputFolder + "/NativeGasEquipment_CritMultiplier.asset",
            DanteNativeGasMigration.OutputFolder + "/NativeGasEquipment_ProjectileCount.asset",
            DanteNativeGasMigration.OutputFolder + "/NativeGasEquipment_Knockback.asset"
        };

        [Test]
        public void Converter_MapsEveryLegacyIdToStableIdAndTypedParameter()
        {
            AssertFloatConversion<DamageStatModifierParameters>(
                LegacyEquipmentModifierConverter.LegacyDamageId,
                DamageStatModifier.ModifierIdValue,
                0.3f,
                parameter => parameter.MultiplierIncrement);
            AssertFloatConversion<SpeedStatModifierParameters>(
                LegacyEquipmentModifierConverter.LegacySpeedId,
                SpeedStatModifier.ModifierIdValue,
                0.4f,
                parameter => parameter.MultiplierIncrement);
            AssertFloatConversion<SizeStatModifierParameters>(
                LegacyEquipmentModifierConverter.LegacySizeId,
                SizeStatModifier.ModifierIdValue,
                0.5f,
                parameter => parameter.MultiplierIncrement);
            AssertFloatConversion<DurationStatModifierParameters>(
                LegacyEquipmentModifierConverter.LegacyDurationId,
                DurationStatModifier.ModifierIdValue,
                0.6f,
                parameter => parameter.MultiplierIncrement);
            AssertFloatConversion<CritRateStatModifierParameters>(
                LegacyEquipmentModifierConverter.LegacyCritRateId,
                CritRateStatModifier.ModifierIdValue,
                0.7f,
                parameter => parameter.MultiplierIncrement);
            AssertFloatConversion<CritMultiplierStatModifierParameters>(
                LegacyEquipmentModifierConverter.LegacyCritMultiplierId,
                CritMultiplierStatModifier.ModifierIdValue,
                0.8f,
                parameter => parameter.MultiplierIncrement);
            AssertFloatConversion<KnockbackStatModifierParameters>(
                LegacyEquipmentModifierConverter.LegacyKnockbackId,
                KnockbackStatModifier.ModifierIdValue,
                0.9f,
                parameter => parameter.MultiplierIncrement);

            EquipmentDataModifier projectile = LegacyEquipmentModifierConverter.Convert(
                LegacyEquipmentModifierConverter.LegacyProjectileCountId,
                2f);
            Assert.That(
                projectile.ModifierIdValue,
                Is.EqualTo(ProjectileCountStatModifier.ModifierIdValue));
            Assert.That(
                ((ProjectileCountStatModifierParameters)projectile.Parameters).CountIncrement,
                Is.EqualTo(2));
        }

        [Test]
        public void Converter_ReadsRecoveredLegacyManagedReferenceParameters()
        {
            EquipmentData damage = AssetDatabase.LoadAssetAtPath<EquipmentData>(
                "Assets/MonoBehaviour/StatRaise_DamageRaiseEquipment.asset");
            EquipmentData knockback = AssetDatabase.LoadAssetAtPath<EquipmentData>(
                "Assets/MonoBehaviour/StatRaise_KnockbackRaiseEquipment.asset");

            Assert.That(damage, Is.Not.Null);
            Assert.That(knockback, Is.Not.Null);

            EquipmentDataModifier damageLevelOne =
                LegacyEquipmentModifierConverter.Convert(damage.Levels[0].Modifiers[0]);
            Assert.That(
                ((DamageStatModifierParameters)damageLevelOne.Parameters).MultiplierIncrement,
                Is.EqualTo(0.3f).Within(0.0001f));

            EquipmentDataModifier knockbackLevelThree =
                LegacyEquipmentModifierConverter.Convert(knockback.Levels[2].Modifiers[0]);
            EquipmentDataModifier speedLevelThree =
                LegacyEquipmentModifierConverter.Convert(knockback.Levels[2].Modifiers[1]);
            Assert.That(
                ((KnockbackStatModifierParameters)knockbackLevelThree.Parameters)
                    .MultiplierIncrement,
                Is.EqualTo(2.2f).Within(0.0001f));
            Assert.That(
                ((SpeedStatModifierParameters)speedLevelThree.Parameters).MultiplierIncrement,
                Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void NativeEquipmentAssets_StoreOnlyStableIdsAndTypedParameters()
        {
            var stableIds = new HashSet<uint>
            {
                DamageStatModifier.ModifierIdValue,
                SpeedStatModifier.ModifierIdValue,
                SizeStatModifier.ModifierIdValue,
                DurationStatModifier.ModifierIdValue,
                CritRateStatModifier.ModifierIdValue,
                CritMultiplierStatModifier.ModifierIdValue,
                ProjectileCountStatModifier.ModifierIdValue,
                KnockbackStatModifier.ModifierIdValue
            };

            for (int assetIndex = 0; assetIndex < NativeEquipmentPaths.Length; assetIndex++)
            {
                NativeGasEquipmentDefinition equipment =
                    AssetDatabase.LoadAssetAtPath<NativeGasEquipmentDefinition>(
                        NativeEquipmentPaths[assetIndex]);
                Assert.That(equipment, Is.Not.Null, NativeEquipmentPaths[assetIndex]);
                Assert.That(equipment.LevelCount, Is.EqualTo(3));

                for (int levelIndex = 0; levelIndex < equipment.LevelCount; levelIndex++)
                {
                    IReadOnlyList<EquipmentDataModifier> modifiers =
                        equipment.GetModifiers(levelIndex);
                    Assert.That(modifiers, Is.Not.Empty);
                    for (int modifierIndex = 0; modifierIndex < modifiers.Count; modifierIndex++)
                    {
                        Assert.That(
                            stableIds.Contains(modifiers[modifierIndex].ModifierIdValue),
                            Is.True,
                            $"Unexpected ID in {equipment.name}, level {levelIndex}.");
                        Assert.That(modifiers[modifierIndex].Parameters, Is.Not.Null);
                    }
                }
            }
        }

        [Test]
        public void DanteWeapon_IsBoundToNativeDefinitionAndPreservesLegacyPresentationPrefab()
        {
            WeaponData weapon = AssetDatabase.LoadAssetAtPath<WeaponData>(
                DanteNativeGasMigration.WeaponPath);
            NativeGasWeaponDefinition definition =
                AssetDatabase.LoadAssetAtPath<NativeGasWeaponDefinition>(
                    DanteNativeGasMigration.NativeWeaponPath);

            Assert.That(weapon, Is.Not.Null);
            Assert.That(definition, Is.Not.Null);
            Assert.That(weapon.NativeGasDefinition, Is.SameAs(definition));
            Assert.That(weapon.WeaponPrefab, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(weapon.WeaponPrefab),
                Is.EqualTo("Assets/GameObject/Dante_SlowProjectile_Behaviour.prefab"));
            Assert.That(definition.CombatId, Is.EqualTo(weapon.ID));
            Assert.That(definition.BaseStats.damage, Is.EqualTo(15));
            Assert.That(definition.BaseStats.speed, Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(definition.BaseStats.knockbackDistance, Is.EqualTo(1f));
            Assert.That(definition.AttackTags & CombatTags.Projectile, Is.Not.Zero);
            Assert.That(
                definition.Supports(new EquipmentModifierID(DurationStatModifier.ModifierIdValue)),
                Is.False,
                "Dante's source modifierFlags intentionally exclude Duration.");
        }

        [Test]
        public void ImportedDantePrefabs_HaveNoMissingMonoBehaviours()
        {
            AssertPrefabHasNoMissingScripts(
                "Assets/GameObject/Dante_SlowProjectile_Behaviour.prefab");
            AssertPrefabHasNoMissingScripts(
                "Assets/GameObject/PlayerAttack_Dante_Projectile.prefab");
            AssertPrefabHasNoMissingScripts(
                "Assets/GameObject/PlayerAttack_Dante_Projectile_Impact.prefab");
        }

        [Test]
        public void NetworkPlayerPrefab_OwnsOnePlayerBuildRuntime()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(
                DanteNativeGasMigration.NetworkPlayerPrefabPath);
            try
            {
                PlayerBuildRuntime[] runtimes =
                    root.GetComponentsInChildren<PlayerBuildRuntime>(true);
                Assert.That(runtimes, Has.Length.EqualTo(1));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void AssertFloatConversion<TParameters>(
            uint legacyId,
            uint stableId,
            float value,
            System.Func<TParameters, float> read)
            where TParameters : EquipmentModifierParameters
        {
            EquipmentDataModifier converted =
                LegacyEquipmentModifierConverter.Convert(legacyId, value);
            Assert.That(converted.ModifierIdValue, Is.EqualTo(stableId));
            Assert.That(converted.Parameters, Is.TypeOf<TParameters>());
            Assert.That(
                read((TParameters)converted.Parameters),
                Is.EqualTo(value).Within(0.0001f));
        }

        private static void AssertPrefabHasNoMissingScripts(string path)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                {
                    Assert.That(
                        GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                            transform.gameObject),
                        Is.Zero,
                        $"Missing script on '{transform.name}' in '{path}'.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
