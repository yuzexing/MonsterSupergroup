using System.Collections.Generic;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Data.Perks;
using AstralShift.HellMaiden.Player.Attacks;
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
        private static readonly string[] CanonicalEquipmentPaths =
        {
            "Assets/MonoBehaviour/StatRaise_DamageRaiseEquipment.asset",
            "Assets/MonoBehaviour/StatRaise_SpeedRaiseEquipment.asset",
            "Assets/MonoBehaviour/StatRaise_SizeRaiseEquipment.asset",
            "Assets/MonoBehaviour/StatRaise_DurationRaiseEquipment.asset",
            "Assets/MonoBehaviour/StatRaise_CritRateEquipment.asset",
            "Assets/MonoBehaviour/StatRaise_CritDamageEquipment.asset",
            "Assets/MonoBehaviour/StatRaise_ProjectileCountRaiseEquipment.asset",
            "Assets/MonoBehaviour/StatRaise_KnockbackRaiseEquipment.asset"
        };

        private static readonly string[] DanteProjectilePrefabPaths =
        {
            "Assets/GameObject/PlayerAttack_Dante_Projectile.prefab",
            "Assets/GameObject/PlayerAttack_Dante_Projectile_Fire Variant.prefab",
            "Assets/GameObject/PlayerAttack_Dante_Projectile_Poison Variant.prefab"
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
        public void PerkConverter_MapsPureWeaponStatsToStableTypedDefinitions()
        {
            AssertPerkFloatConversion<WeaponDamagePerkModifierParameters>(
                LegacyPerkModifierConverter.LegacyWeaponDamageId,
                WeaponDamagePerkModifier.ModifierIdValue,
                0.11f,
                parameter => parameter.MultiplierIncrement);
            AssertPerkFloatConversion<WeaponSpeedPerkModifierParameters>(
                LegacyPerkModifierConverter.LegacyWeaponSpeedId,
                WeaponSpeedPerkModifier.ModifierIdValue,
                0.12f,
                parameter => parameter.MultiplierIncrement);
            AssertPerkFloatConversion<WeaponSizePerkModifierParameters>(
                LegacyPerkModifierConverter.LegacyWeaponSizeId,
                WeaponSizePerkModifier.ModifierIdValue,
                0.13f,
                parameter => parameter.MultiplierIncrement);
            AssertPerkFloatConversion<WeaponDurationPerkModifierParameters>(
                LegacyPerkModifierConverter.LegacyWeaponDurationId,
                WeaponDurationPerkModifier.ModifierIdValue,
                0.14f,
                parameter => parameter.MultiplierIncrement);
            AssertPerkFloatConversion<WeaponCritRatePerkModifierParameters>(
                LegacyPerkModifierConverter.LegacyWeaponCritRateId,
                WeaponCritRatePerkModifier.ModifierIdValue,
                0.15f,
                parameter => parameter.MultiplierIncrement);
            AssertPerkFloatConversion<WeaponCritMultiplierPerkModifierParameters>(
                LegacyPerkModifierConverter.LegacyWeaponCritMultiplierId,
                WeaponCritMultiplierPerkModifier.ModifierIdValue,
                0.16f,
                parameter => parameter.MultiplierIncrement);

            PerkDataModifier projectile = LegacyPerkModifierConverter.Convert(
                LegacyPerkModifierConverter.LegacyProjectileCountId,
                2f);
            Assert.That(
                projectile.ModifierIdValue,
                Is.EqualTo(WeaponProjectileCountPerkModifier.ModifierIdValue));
            Assert.That(
                ((WeaponProjectileCountPerkModifierParameters)projectile.Parameters)
                    .CountIncrement,
                Is.EqualTo(2));
        }

        [Test]
        public void CanonicalPureWeaponPerks_UseStableIdsAndSourceAssetGuids()
        {
            string[] expectedGuids =
            {
                "76fa5ea1218be2a438ce7f47501f02b0",
                "7f2deebf37c7862418b2165ea331a3af",
                "88c4f782fea6f734b9cf2d426adf1544",
                "36d51e4b34f9c67498f4ed664339ef32",
                "444dd71615735a94fabde1c29d4047bc",
                "7fb8c340e2637844981ab950c5a53d2c",
                "6cc353e437573ad4ba9c894a2a47c1f4"
            };
            var stableIds = new HashSet<uint>
            {
                WeaponDamagePerkModifier.ModifierIdValue,
                WeaponSpeedPerkModifier.ModifierIdValue,
                WeaponSizePerkModifier.ModifierIdValue,
                WeaponDurationPerkModifier.ModifierIdValue,
                WeaponCritRatePerkModifier.ModifierIdValue,
                WeaponCritMultiplierPerkModifier.ModifierIdValue,
                WeaponProjectileCountPerkModifier.ModifierIdValue
            };
            IReadOnlyList<string> paths =
                PureWeaponPerkMigration.CanonicalAssetPaths;

            Assert.That(paths.Count, Is.EqualTo(expectedGuids.Length));
            for (int pathIndex = 0; pathIndex < paths.Count; pathIndex++)
            {
                PerkData perk = AssetDatabase.LoadAssetAtPath<PerkData>(
                    paths[pathIndex]);
                Assert.That(perk, Is.Not.Null, paths[pathIndex]);
                Assert.That(
                    AssetDatabase.AssetPathToGUID(paths[pathIndex]),
                    Is.EqualTo(expectedGuids[pathIndex]),
                    paths[pathIndex]);
                Assert.DoesNotThrow(perk.ValidateNativeGas);

                PerkRarityModifiersData[] rarities = perk.GetAllRarities();
                for (int rarityIndex = 0;
                    rarityIndex < rarities.Length;
                    rarityIndex++)
                {
                    PerkModifierApplication[] applications =
                        rarities[rarityIndex].Modifiers;
                    for (int modifierIndex = 0;
                        modifierIndex < applications.Length;
                        modifierIndex++)
                    {
                        Assert.That(
                            applications[modifierIndex].Domain,
                            Is.EqualTo(PerkApplicationDomain.WeaponStats));
                        Assert.That(
                            stableIds.Contains(
                                applications[modifierIndex].ModifierIdValue),
                            Is.True,
                            paths[pathIndex]);
                        Assert.That(
                            applications[modifierIndex].Parameters,
                            Is.Not.Null,
                            paths[pathIndex]);
                    }
                }
            }

            PerkDB database = AssetDatabase.LoadAssetAtPath<PerkDB>(
                PureWeaponPerkMigration.DatabasePath);
            Assert.That(database, Is.Not.Null);
            Assert.That(database.Perks, Has.Length.EqualTo(paths.Count));
        }

        [Test]
        public void CanonicalAssets_PreserveConvertedLegacyParameterValues()
        {
            EquipmentData damage = AssetDatabase.LoadAssetAtPath<EquipmentData>(
                "Assets/MonoBehaviour/StatRaise_DamageRaiseEquipment.asset");
            EquipmentData knockback = AssetDatabase.LoadAssetAtPath<EquipmentData>(
                "Assets/MonoBehaviour/StatRaise_KnockbackRaiseEquipment.asset");

            Assert.That(damage, Is.Not.Null);
            Assert.That(knockback, Is.Not.Null);

            EquipmentDataModifier damageLevelOne =
                damage.Levels[0].Modifiers[0].Modifier;
            Assert.That(
                ((DamageStatModifierParameters)damageLevelOne.Parameters).MultiplierIncrement,
                Is.EqualTo(0.3f).Within(0.0001f));

            EquipmentDataModifier knockbackLevelThree =
                knockback.Levels[2].Modifiers[0].Modifier;
            EquipmentDataModifier speedLevelThree =
                knockback.Levels[2].Modifiers[1].Modifier;
            Assert.That(
                ((KnockbackStatModifierParameters)knockbackLevelThree.Parameters)
                    .MultiplierIncrement,
                Is.EqualTo(2.2f).Within(0.0001f));
            Assert.That(
                ((SpeedStatModifierParameters)speedLevelThree.Parameters).MultiplierIncrement,
                Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void CanonicalEquipmentAssets_StoreOnlyStableIdsAndTypedParameters()
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

            for (int assetIndex = 0;
                assetIndex < CanonicalEquipmentPaths.Length;
                assetIndex++)
            {
                EquipmentData equipment = AssetDatabase.LoadAssetAtPath<EquipmentData>(
                    CanonicalEquipmentPaths[assetIndex]);
                Assert.That(equipment, Is.Not.Null, CanonicalEquipmentPaths[assetIndex]);
                Assert.That(equipment.Levels, Has.Length.EqualTo(3));

                for (int levelIndex = 0;
                    levelIndex < equipment.Levels.Length;
                    levelIndex++)
                {
                    IReadOnlyList<EquipmentModifierApplication> modifiers =
                        equipment.Levels[levelIndex].Modifiers;
                    Assert.That(modifiers, Is.Not.Empty);
                    for (int modifierIndex = 0;
                        modifierIndex < modifiers.Count;
                        modifierIndex++)
                    {
                        Assert.That(
                            stableIds.Contains(
                                modifiers[modifierIndex].ModifierIdValue),
                            Is.True,
                            $"Unexpected ID in {equipment.name}, level {levelIndex}.");
                        Assert.That(modifiers[modifierIndex].Parameters, Is.Not.Null);
                    }
                }
            }

            Assert.That(
                AssetDatabase.FindAssets(
                    "t:NativeGasEquipmentDefinition",
                    new[] { DanteNativeGasMigration.OutputFolder }),
                Is.Empty,
                "Canonical EquipmentData assets must not have Native GAS duplicates.");
        }

        [Test]
        public void DanteWeapon_IsCanonicalNativeGasDefinitionAndPreservesPresentationPrefab()
        {
            WeaponData weapon = AssetDatabase.LoadAssetAtPath<WeaponData>(
                DanteNativeGasMigration.WeaponPath);

            Assert.That(weapon, Is.Not.Null);
            Assert.DoesNotThrow(weapon.ValidateNativeGas);
            Assert.That(weapon.WeaponPrefab, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(weapon.WeaponPrefab),
                Is.EqualTo("Assets/GameObject/Dante_SlowProjectile_Behaviour.prefab"));
            Assert.That(weapon.ID, Is.EqualTo(2u));
            Assert.That(weapon.BaseStats.damage, Is.EqualTo(15));
            Assert.That(weapon.BaseStats.speed, Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(weapon.BaseStats.knockbackDistance, Is.EqualTo(1f));
            Assert.That(weapon.AttackTags & CombatTags.Projectile, Is.Not.Zero);
            Assert.That(
                weapon.Supports(new EquipmentModifierID(DurationStatModifier.ModifierIdValue)),
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
        public void ImportedDanteProjectilePrefabs_HaveRecoveredRuntimeReferences()
        {
            for (int i = 0; i < DanteProjectilePrefabPaths.Length; i++)
            {
                string path = DanteProjectilePrefabPaths[i];
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    ProjectileAttack projectile = root.GetComponent<ProjectileAttack>();
                    Assert.That(projectile, Is.Not.Null, path);
                    Assert.That(projectile.progressionScaler, Is.Not.Null, path);
                    Assert.That(projectile.hitbox, Is.Not.Null, path);
                    Assert.That(projectile.hitEffectResolver, Is.Not.Null, path);
                    Assert.That(projectile.RotationTransform, Is.Not.Null, path);

                    var serializedProjectile = new SerializedObject(projectile);
                    SerializedProperty animancer =
                        serializedProjectile.FindProperty("animancer");
                    Assert.That(animancer, Is.Not.Null, path);
                    Assert.That(animancer.objectReferenceValue, Is.Not.Null, path);
                    SerializedProperty particleSystem =
                        serializedProjectile.FindProperty("particleSystem");
                    Assert.That(particleSystem, Is.Not.Null, path);
                    Assert.That(
                        particleSystem.objectReferenceValue,
                        Is.Not.Null,
                        path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
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
                Assert.That(runtimes[0].InitialWeaponId, Is.EqualTo(2u));
                Assert.That(root.GetComponent("PlayerHandBehaviour"), Is.Null);
                Assert.That(root.GetComponent("PlayerLoader"), Is.Null);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void NativeWeaponDatabase_ContainsMigratedDanteWeapon()
        {
            WeaponDB database = AssetDatabase.LoadAssetAtPath<WeaponDB>(
                DanteNativeGasMigration.NativeWeaponDatabasePath);
            WeaponData weapon = AssetDatabase.LoadAssetAtPath<WeaponData>(
                DanteNativeGasMigration.WeaponPath);

            Assert.That(database, Is.Not.Null);
            Assert.That(database.Weapons, Does.Contain(weapon));
            Assert.DoesNotThrow(weapon.ValidateNativeGas);
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

        private static void AssertPerkFloatConversion<TParameters>(
            uint legacyId,
            uint stableId,
            float value,
            System.Func<TParameters, float> read)
            where TParameters : PerkModifierParameters
        {
            PerkDataModifier converted =
                LegacyPerkModifierConverter.Convert(legacyId, value);
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
