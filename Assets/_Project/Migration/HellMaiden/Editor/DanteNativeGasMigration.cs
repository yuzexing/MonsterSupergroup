using System;
using System.Collections.Generic;
using System.IO;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.HellMaidenMigration.Editor
{
    public static class DanteNativeGasMigration
    {
        public const string WeaponPath =
            "Assets/MonoBehaviour/WeaponData_Dante_SlowProjectile.asset";
        public const string OutputFolder =
            "Assets/_Project/Content/HellMaiden/NativeGAS/Dante";
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
            NormalizeAnimancerComponentReferences();
            RepairDanteProjectilePrefabReferences();
            EnsureFolder(OutputFolder);

            WeaponData weapon = RequireAsset<WeaponData>(WeaponPath);
            ConfigureWeapon(weapon);

            for (int i = 0; i < EquipmentMigrations.Length; i++)
            {
                ConvertEquipment(EquipmentMigrations[i]);
            }

            EditorUtility.SetDirty(weapon);
            EnsureNativeWeaponDatabase(weapon);
            EnsurePlayerBuildRuntime(NetworkPlayerPrefabPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                "Dante native GAS assets rebuilt. Legacy modifier IDs were consumed only " +
                "by the Editor converter; runtime definitions contain stable IDs.");
        }

        [MenuItem("Tools/HellMaiden Migration/Reserialize Canonical Equipment Assets")]
        public static void ReserializeCanonicalEquipmentAssets()
        {
            var paths = new List<string>(EquipmentMigrations.Length);
            for (int i = 0; i < EquipmentMigrations.Length; i++)
            {
                paths.Add(EquipmentMigrations[i].SourcePath);
            }

            AssetDatabase.ForceReserializeAssets(
                paths,
                ForceReserializeAssetsOptions.ReserializeAssetsAndMetadata);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Canonical EquipmentData assets reserialized without legacy fields.");
        }

        private static void ConfigureWeapon(WeaponData weapon)
        {
            weapon.ValidateNativeGas();
            if ((weapon.AttackTags & CombatTags.Projectile) == 0)
            {
                throw new InvalidOperationException(
                    $"Canonical Dante weapon '{weapon.name}' is missing the Projectile tag.");
            }
        }

        private static void ConvertEquipment(EquipmentMigration migration)
        {
            EquipmentData source = RequireAsset<EquipmentData>(migration.SourcePath);
            if (source.Levels == null || source.Levels.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Canonical equipment '{source.name}' contains no levels.");
            }

            for (int levelIndex = 0; levelIndex < source.Levels.Length; levelIndex++)
            {
                EquipmentLevelModifiersData sourceLevel = source.Levels[levelIndex]
                    ?? throw new InvalidOperationException(
                        $"Canonical equipment '{source.name}' has a null level {levelIndex}.");
                EquipmentModifierApplication[] applications = sourceLevel.Modifiers;
                if (applications.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"Canonical equipment '{source.name}' level {levelIndex} has no modifiers.");
                }

                for (int modifierIndex = 0;
                    modifierIndex < applications.Length;
                    modifierIndex++)
                {
                    EquipmentModifierApplication application =
                        applications[modifierIndex]
                        ?? throw new InvalidOperationException(
                            $"Canonical equipment '{source.name}' level {levelIndex} " +
                            $"has a null modifier at index {modifierIndex}.");
                    if (application.Modifier == null ||
                        !application.ModifierId.IsValid ||
                        application.Parameters == null)
                    {
                        throw new InvalidOperationException(
                            $"Canonical equipment '{source.name}' level {levelIndex}, " +
                            $"modifier {modifierIndex} is incomplete.");
                    }
                }
            }

            string duplicatePath = OutputFolder + "/" + migration.OutputName;
            if (AssetDatabase.LoadMainAssetAtPath(duplicatePath) != null)
            {
                AssetDatabase.DeleteAsset(duplicatePath);
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
