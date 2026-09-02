using System;
using System.Collections.Generic;
using System.IO;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using MonsterSupergroup.GAS;
using MonsterSupergroup.GAS.Authoring;
using MonsterSupergroup.Gameplay.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;
using LegacyAttackStats = AstralShift.HellMaiden.Player.Attacks.AttackStats;
using NativeAttackStats = MonsterSupergroup.GAS.AttackStats;

namespace MonsterSupergroup.HellMaidenMigration.Editor
{
    public static class DanteNativeGasMigration
    {
        public const string WeaponPath =
            "Assets/MonoBehaviour/WeaponData_Dante_SlowProjectile.asset";
        public const string OutputFolder =
            "Assets/_Project/Content/HellMaiden/NativeGAS/Dante";
        public const string NativeWeaponPath =
            OutputFolder + "/NativeGasWeapon_Dante_SlowProjectile.asset";
        public const string NetworkPlayerPrefabPath =
            "Assets/_Project/Content/NetworkCombat/NetworkPlayer.prefab";
        public const string NativeWeaponDatabasePath =
            "Assets/_Project/Content/HellMaiden/NativeGAS/NativeGasWeaponDB.asset";

        private static readonly string[] DanteProjectilePrefabPaths =
        {
            "Assets/GameObject/PlayerAttack_Dante_Projectile.prefab",
            "Assets/GameObject/PlayerAttack_Dante_Projectile_Fire Variant.prefab",
            "Assets/GameObject/PlayerAttack_Dante_Projectile_Poison Variant.prefab"
        };

        private static readonly EquipmentMigration[] EquipmentMigrations =
        {
            new EquipmentMigration(
                "Assets/MonoBehaviour/StatRaise_DamageRaiseEquipment.asset",
                "NativeGasEquipment_Damage.asset"),
            new EquipmentMigration(
                "Assets/MonoBehaviour/StatRaise_SpeedRaiseEquipment.asset",
                "NativeGasEquipment_Speed.asset"),
            new EquipmentMigration(
                "Assets/MonoBehaviour/StatRaise_SizeRaiseEquipment.asset",
                "NativeGasEquipment_Size.asset"),
            new EquipmentMigration(
                "Assets/MonoBehaviour/StatRaise_DurationRaiseEquipment.asset",
                "NativeGasEquipment_Duration.asset"),
            new EquipmentMigration(
                "Assets/MonoBehaviour/StatRaise_CritRateEquipment.asset",
                "NativeGasEquipment_CritRate.asset"),
            new EquipmentMigration(
                "Assets/MonoBehaviour/StatRaise_CritDamageEquipment.asset",
                "NativeGasEquipment_CritMultiplier.asset"),
            new EquipmentMigration(
                "Assets/MonoBehaviour/StatRaise_ProjectileCountRaiseEquipment.asset",
                "NativeGasEquipment_ProjectileCount.asset"),
            new EquipmentMigration(
                "Assets/MonoBehaviour/StatRaise_KnockbackRaiseEquipment.asset",
                "NativeGasEquipment_Knockback.asset")
        };

        [MenuItem("Tools/HellMaiden Migration/Rebuild Dante Native GAS Assets")]
        public static void Rebuild()
        {
            NormalizeLegacyManagedReferenceAssemblies();
            NormalizeAnimancerComponentReferences();
            RepairDanteProjectilePrefabReferences();
            EnsureFolder(OutputFolder);

            WeaponData weapon = RequireAsset<WeaponData>(WeaponPath);
            NativeGasWeaponDefinition nativeWeapon =
                LoadOrCreate<NativeGasWeaponDefinition>(NativeWeaponPath);
            ConfigureWeapon(weapon, nativeWeapon);

            for (int i = 0; i < EquipmentMigrations.Length; i++)
            {
                ConvertEquipment(EquipmentMigrations[i]);
            }

            weapon.SetNativeGasDefinition(nativeWeapon);
            EditorUtility.SetDirty(weapon);
            EnsureNativeWeaponDatabase(weapon);
            EnsurePlayerBuildRuntime(NetworkPlayerPrefabPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                "Dante native GAS assets rebuilt. Legacy modifier IDs were consumed only " +
                "by the Editor converter; runtime definitions contain stable IDs.");
        }

        private static void ConfigureWeapon(
            WeaponData source,
            NativeGasWeaponDefinition destination)
        {
            LegacyAttackStats legacy = source.BaseStats;
            var stats = new NativeAttackStats
            {
                damage = legacy.damage,
                critMultiplier = legacy.critMultiplier,
                critRate = legacy.critRate,
                speed = legacy.speed,
                size = legacy.size,
                duration = legacy.duration,
                projectileCount = legacy.projectileCount,
                knockbackDistance = legacy.knockbackSettings != null
                    ? legacy.knockbackSettings.distance
                    : 0f,
                damageType = (MonsterSupergroup.GAS.DamageType)(int)legacy.damageType
            };

            destination.Configure(
                source.ID,
                stats,
                BuildTags(legacy.damageType),
                source.modifierFlags,
                legacy.knockbackSettings);
            EditorUtility.SetDirty(destination);
        }

        private static CombatTags BuildTags(
            AstralShift.HellMaiden.Player.Attacks.DamageType damageType)
        {
            CombatTags tags = CombatTags.Attack | CombatTags.Projectile;
            switch (damageType)
            {
                case AstralShift.HellMaiden.Player.Attacks.DamageType.Fire:
                    return tags | CombatTags.Fire;
                case AstralShift.HellMaiden.Player.Attacks.DamageType.Poison:
                    return tags | CombatTags.Poison;
                default:
                    return tags;
            }
        }

        private static void ConvertEquipment(EquipmentMigration migration)
        {
            EquipmentData source = RequireAsset<EquipmentData>(migration.SourcePath);
            if (source.Levels == null || source.Levels.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Legacy equipment '{source.name}' contains no levels.");
            }

            var levels = new List<NativeGasEquipmentLevel>(source.Levels.Length);
            for (int levelIndex = 0; levelIndex < source.Levels.Length; levelIndex++)
            {
                EquipmentLevelModifiersData sourceLevel = source.Levels[levelIndex]
                    ?? throw new InvalidOperationException(
                        $"Legacy equipment '{source.name}' has a null level {levelIndex}.");
                if (sourceLevel.Modifiers == null)
                {
                    throw new InvalidOperationException(
                        $"Legacy equipment '{source.name}' level {levelIndex} has null modifiers.");
                }

                var modifiers = new List<MonsterSupergroup.GAS.Authoring.EquipmentDataModifier>(
                    sourceLevel.Modifiers.Length);
                for (int modifierIndex = 0;
                    modifierIndex < sourceLevel.Modifiers.Length;
                    modifierIndex++)
                {
                    modifiers.Add(LegacyEquipmentModifierConverter.Convert(
                        sourceLevel.Modifiers[modifierIndex]));
                }

                var level = new NativeGasEquipmentLevel();
                level.Configure(modifiers);
                levels.Add(level);
            }

            string destinationPath = OutputFolder + "/" + migration.OutputName;
            NativeGasEquipmentDefinition destination =
                LoadOrCreate<NativeGasEquipmentDefinition>(destinationPath);
            if (HasSameEquipmentData(destination, levels))
            {
                return;
            }

            // Flush the old managed-reference table before assigning deterministic
            // IDs to replacement parameter objects. Unity otherwise keeps the old
            // objects registered until the host asset is serialized.
            destination.Configure(Array.Empty<NativeGasEquipmentLevel>());
            EditorUtility.SetDirty(destination);
            AssetDatabase.SaveAssetIfDirty(destination);

            destination.Configure(levels);
            AssignDeterministicManagedReferenceIds(destination, levels);
            EditorUtility.SetDirty(destination);
        }

        private static bool HasSameEquipmentData(
            NativeGasEquipmentDefinition destination,
            IReadOnlyList<NativeGasEquipmentLevel> expectedLevels)
        {
            if (destination.LevelCount != expectedLevels.Count)
            {
                return false;
            }

            for (int levelIndex = 0; levelIndex < expectedLevels.Count; levelIndex++)
            {
                IReadOnlyList<MonsterSupergroup.GAS.Authoring.EquipmentDataModifier>
                    actual = destination.GetModifiers(levelIndex);
                IReadOnlyList<MonsterSupergroup.GAS.Authoring.EquipmentDataModifier>
                    expected = expectedLevels[levelIndex].Modifiers;
                if (actual.Count != expected.Count)
                {
                    return false;
                }

                for (int modifierIndex = 0; modifierIndex < expected.Count; modifierIndex++)
                {
                    MonsterSupergroup.GAS.Authoring.EquipmentDataModifier actualModifier =
                        actual[modifierIndex];
                    MonsterSupergroup.GAS.Authoring.EquipmentDataModifier expectedModifier =
                        expected[modifierIndex];
                    if (actualModifier.ModifierIdValue != expectedModifier.ModifierIdValue ||
                        actualModifier.Parameters == null ||
                        expectedModifier.Parameters == null ||
                        actualModifier.Parameters.GetType() !=
                            expectedModifier.Parameters.GetType() ||
                        JsonUtility.ToJson(actualModifier.Parameters) !=
                            JsonUtility.ToJson(expectedModifier.Parameters))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static void AssignDeterministicManagedReferenceIds(
            NativeGasEquipmentDefinition destination,
            IReadOnlyList<NativeGasEquipmentLevel> levels)
        {
            for (int levelIndex = 0; levelIndex < levels.Count; levelIndex++)
            {
                IReadOnlyList<MonsterSupergroup.GAS.Authoring.EquipmentDataModifier>
                    modifiers = levels[levelIndex].Modifiers;
                for (int modifierIndex = 0; modifierIndex < modifiers.Count; modifierIndex++)
                {
                    long referenceId = (levelIndex + 1L) * 1000L + modifierIndex + 1L;
                    if (!ManagedReferenceUtility.SetManagedReferenceIdForObject(
                        destination,
                        modifiers[modifierIndex].Parameters,
                        referenceId))
                    {
                        throw new InvalidOperationException(
                            $"Could not assign deterministic managed-reference ID " +
                            $"{referenceId} in '{destination.name}'.");
                    }
                }
            }
        }

        private static void NormalizeLegacyManagedReferenceAssemblies()
        {
            const string oldAssembly = ", asm: Assembly-CSharp}";
            const string recoveredAssembly =
                ", asm: MonsterSupergroup.Gameplay.Combat}";
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Could not resolve the Unity project root.");

            for (int i = 0; i < EquipmentMigrations.Length; i++)
            {
                string assetPath = EquipmentMigrations[i].SourcePath;
                string fullPath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));
                if (!File.Exists(fullPath))
                {
                    throw new FileNotFoundException(
                        "Required legacy equipment source is missing.",
                        fullPath);
                }

                string yaml = File.ReadAllText(fullPath);
                string normalized = yaml.Replace(oldAssembly, recoveredAssembly);
                if (yaml != normalized)
                {
                    File.WriteAllText(fullPath, normalized);
                    AssetDatabase.ImportAsset(
                        assetPath,
                        ImportAssetOptions.ForceSynchronousImport |
                        ImportAssetOptions.ForceUpdate);
                }
            }
        }

        private static void NormalizeAnimancerComponentReferences()
        {
            const string oldDllReference =
                "{fileID: -536417829, guid: a764a8f1aa53ec5f484d2a941db13b66, type: 3}";
            const string officialPackageReference =
                "{fileID: 11500000, guid: 0ad50f81b1d25c441943c37a89ba23f6, type: 3}";
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Could not resolve the Unity project root.");

            for (int i = 0; i < DanteProjectilePrefabPaths.Length; i++)
            {
                string prefabPath = DanteProjectilePrefabPaths[i];
                string fullPath = Path.GetFullPath(Path.Combine(projectRoot, prefabPath));
                if (!File.Exists(fullPath))
                {
                    throw new FileNotFoundException(
                        "Required Dante presentation prefab is missing.",
                        fullPath);
                }

                string yaml = File.ReadAllText(fullPath);
                string normalized = yaml.Replace(oldDllReference, officialPackageReference);
                if (yaml != normalized)
                {
                    File.WriteAllText(fullPath, normalized);
                    AssetDatabase.ImportAsset(
                        prefabPath,
                        ImportAssetOptions.ForceSynchronousImport |
                        ImportAssetOptions.ForceUpdate);
                }
            }
        }

        private static void RepairDanteProjectilePrefabReferences()
        {
            for (int i = 0; i < DanteProjectilePrefabPaths.Length; i++)
            {
                string prefabPath = DanteProjectilePrefabPaths[i];
                if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
                {
                    throw new InvalidOperationException(
                        $"Required Dante presentation prefab is missing: {prefabPath}");
                }

                GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    ProjectileAttack projectile = root.GetComponent<ProjectileAttack>()
                        ?? throw new InvalidOperationException(
                            $"'{prefabPath}' contains no ProjectileAttack component.");
                    AttackProgressionScaler progressionScaler =
                        root.GetComponent<AttackProgressionScaler>()
                        ?? throw new InvalidOperationException(
                            $"'{prefabPath}' contains no AttackProgressionScaler component.");
                    PlayerAttackHitBox hitbox =
                        root.GetComponentInChildren<PlayerAttackHitBox>(true)
                        ?? throw new InvalidOperationException(
                            $"'{prefabPath}' contains no PlayerAttackHitBox component.");
                    SpawnableHitEffectResolver hitEffectResolver =
                        root.GetComponentInChildren<SpawnableHitEffectResolver>(true)
                        ?? throw new InvalidOperationException(
                            $"'{prefabPath}' contains no SpawnableHitEffectResolver component.");
                    Component animancer = root.GetComponent("AnimancerComponent")
                        ?? throw new InvalidOperationException(
                            $"'{prefabPath}' contains no AnimancerComponent.");
                    ParticleSystem particleSystem = root.GetComponent<ParticleSystem>()
                        ?? throw new InvalidOperationException(
                            $"'{prefabPath}' contains no root ParticleSystem component.");
                    Transform rotationPivot = root.transform.Find("Root")
                        ?? throw new InvalidOperationException(
                            $"'{prefabPath}' contains no 'Root' visual pivot.");

                    var serializedProjectile = new SerializedObject(projectile);
                    SetRequiredReference(
                        serializedProjectile,
                        "progressionScaler",
                        progressionScaler,
                        prefabPath);
                    SetRequiredReference(
                        serializedProjectile,
                        "hitbox",
                        hitbox,
                        prefabPath);
                    SetRequiredReference(
                        serializedProjectile,
                        "hitEffectResolver",
                        hitEffectResolver,
                        prefabPath);
                    SetRequiredReference(
                        serializedProjectile,
                        "rotationTransform",
                        rotationPivot,
                        prefabPath);
                    SetRequiredReference(
                        serializedProjectile,
                        "animancer",
                        animancer,
                        prefabPath);
                    SetRequiredReference(
                        serializedProjectile,
                        "particleSystem",
                        particleSystem,
                        prefabPath);
                    serializedProjectile.ApplyModifiedPropertiesWithoutUndo();

                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        private static void SetRequiredReference(
            SerializedObject target,
            string propertyName,
            UnityEngine.Object value,
            string assetPath)
        {
            SerializedProperty property = target.FindProperty(propertyName)
                ?? throw new InvalidOperationException(
                    $"'{assetPath}' has no serialized ProjectileAttack property " +
                    $"'{propertyName}'.");
            property.objectReferenceValue = value;
        }

        private static void EnsurePlayerBuildRuntime(string prefabPath)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                throw new InvalidOperationException(
                    $"Network player prefab is missing: {prefabPath}");
            }

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                PlayerMovement player = root.GetComponentInChildren<PlayerMovement>(true)
                    ?? throw new InvalidOperationException(
                        $"'{prefabPath}' contains no PlayerMovement.");
                PlayerBuildRuntime build = player.GetComponent<PlayerBuildRuntime>();
                if (build == null)
                {
                    build = player.gameObject.AddComponent<PlayerBuildRuntime>();
                }

                build.ConfigureInitialWeapon(2u);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void EnsureNativeWeaponDatabase(WeaponData weapon)
        {
            WeaponDB database = LoadOrCreate<WeaponDB>(NativeWeaponDatabasePath);
            var weapons = new List<WeaponData>();
            WeaponData[] existing = database.Weapons;
            bool replaced = false;
            if (existing != null)
            {
                for (int i = 0; i < existing.Length; i++)
                {
                    WeaponData entry = existing[i];
                    if (entry == null)
                    {
                        continue;
                    }

                    if (entry.ID == weapon.ID)
                    {
                        if (!replaced)
                        {
                            weapons.Add(weapon);
                            replaced = true;
                        }

                        continue;
                    }

                    weapons.Add(entry);
                }
            }

            if (!replaced)
            {
                weapons.Add(weapon);
            }

            database.Configure(weapons.ToArray());
            EditorUtility.SetDirty(database);
        }

        private static T RequireAsset<T>(string path)
            where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new InvalidOperationException(
                    $"Required Dante migration asset is missing or has the wrong type: {path}");
            }

            return asset;
        }

        private static T LoadOrCreate<T>(string path)
            where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void EnsureFolder(string folder)
        {
            string[] segments = folder.Split('/');
            string current = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string next = current + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[i]);
                }

                current = next;
            }
        }

        private readonly struct EquipmentMigration
        {
            public EquipmentMigration(string sourcePath, string outputName)
            {
                SourcePath = sourcePath;
                OutputName = outputName;
            }

            public string SourcePath { get; }
            public string OutputName { get; }
        }
    }
}
