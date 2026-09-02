using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Data.Perks;
using MonsterSupergroup.GAS;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.HellMaidenMigration.Editor
{
    public static class PureWeaponPerkMigration
    {
        public const string DatabasePath =
            "Assets/_Project/Content/HellMaiden/NativeGAS/NativeGasPerkDB.asset";

        private static readonly PerkMigration[] Migrations =
        {
            new PerkMigration(
                "Assets/MonoBehaviour/AllDamagePerk.asset",
                2u,
                "All Damage",
                "Adds {PlayerDamage}[0]% more damage to all weapons.",
                "PRK_Name_002",
                "PRK_Desc_002",
                "13877d7b4f7178b498eaec2d14951f3f",
                "PlayerDamage",
                LegacyPerkModifierConverter.LegacyWeaponDamageId,
                new[] { PerkRarity.Bronze, PerkRarity.Silver, PerkRarity.Gold },
                new[] { 0.05f, 0.07f, 0.10f }),
            new PerkMigration(
                "Assets/MonoBehaviour/AttackSpeedPerk.asset",
                3u,
                "Attack Speed",
                "Raises Attack Speed by {PlayerAttackSpeed}[0]%.",
                "PRK_Name_003",
                "PRK_Desc_003",
                "db737da432e935044b6e9ac59f2a8aea",
                "PlayerAttackSpeed",
                LegacyPerkModifierConverter.LegacyWeaponSpeedId,
                new[] { PerkRarity.Bronze, PerkRarity.Silver, PerkRarity.Gold },
                new[] { 0.05f, 0.07f, 0.10f }),
            new PerkMigration(
                "Assets/MonoBehaviour/ExtraWeaponSizePerk.asset",
                22u,
                "Weapon Size",
                "Adds {WeaponSize}[0]%  Weapon Size.",
                "PRK_Name_022",
                "PRK_Desc_022",
                "7ae278d7d8146fd46aef6f73774a48e6",
                "WeaponSize",
                LegacyPerkModifierConverter.LegacyWeaponSizeId,
                new[] { PerkRarity.Bronze, PerkRarity.Silver, PerkRarity.Gold },
                new[] { 0.10f, 0.14f, 0.20f }),
            new PerkMigration(
                "Assets/MonoBehaviour/ExtraWeaponDurationPerk.asset",
                19u,
                "Extra Weapon Duration",
                "Adds {WeaponDuration}[0]%  Weapon Duration.",
                "PRK_Name_019",
                "PRK_Desc_019",
                "165a2185e76b84546bf8efcab5dbaec6",
                "WeaponDuration",
                LegacyPerkModifierConverter.LegacyWeaponDurationId,
                new[] { PerkRarity.Bronze, PerkRarity.Silver, PerkRarity.Gold },
                new[] { 0.10f, 0.14f, 0.20f }),
            new PerkMigration(
                "Assets/MonoBehaviour/ExtraCriteRatePerk.asset",
                18u,
                "Extra Crit Rate",
                "Adds {CritRate}[0]% raises crit Rate.",
                "PRK_Name_018",
                "PRK_Desc_018",
                "2bf69392be039864f8ac3d44ca06d298",
                "CritRate",
                LegacyPerkModifierConverter.LegacyWeaponCritRateId,
                new[] { PerkRarity.Silver, PerkRarity.Gold },
                new[] { 0.03f, 0.06f }),
            new PerkMigration(
                "Assets/MonoBehaviour/ExtraCritMultiplierPerk.asset",
                17u,
                "Extra Crit Multiplier",
                "Adds {CritMultiplier}[0]% to crit Multiplier.",
                "PRK_Name_017",
                "PRK_Desc_017",
                "e849de5503ed921459971e9ba2920403",
                "CritMultiplier",
                LegacyPerkModifierConverter.LegacyWeaponCritMultiplierId,
                new[] { PerkRarity.Bronze, PerkRarity.Silver, PerkRarity.Gold },
                new[] { 0.10f, 0.14f, 0.20f }),
            new PerkMigration(
                "Assets/MonoBehaviour/ExtraProjectilePerk.asset",
                28u,
                "Extra Projectile",
                "+1 Projectile to everything",
                "PRK_Name_028",
                "PRK_Desc_028",
                "f2f93ff876c341b4281e0352d9bf70b5",
                string.Empty,
                LegacyPerkModifierConverter.LegacyProjectileCountId,
                new[] { PerkRarity.Crystal },
                new[] { 1f })
        };

        public static IReadOnlyList<string> CanonicalAssetPaths
        {
            get
            {
                var paths = new string[Migrations.Length];
                for (int i = 0; i < Migrations.Length; i++)
                {
                    paths[i] = Migrations[i].AssetPath;
                }

                return paths;
            }
        }

        [MenuItem("Tools/HellMaiden Migration/Rebuild Pure Weapon Perks")]
        public static void Rebuild()
        {
            var perks = new PerkData[Migrations.Length];
            for (int i = 0; i < Migrations.Length; i++)
            {
                perks[i] = Rebuild(Migrations[i]);
            }

            EnsureFolder("Assets/_Project/Content/HellMaiden/NativeGAS");
            PerkDB database = AssetDatabase.LoadAssetAtPath<PerkDB>(DatabasePath);
            if (database == null)
            {
                database = ScriptableObject.CreateInstance<PerkDB>();
                AssetDatabase.CreateAsset(database, DatabasePath);
            }

            database.Perks = perks;
            EditorUtility.SetDirty(database);

            var paths = new List<string>(CanonicalAssetPaths) { DatabasePath };
            AssetDatabase.SaveAssets();
            AssetDatabase.ForceReserializeAssets(
                paths,
                ForceReserializeAssetsOptions.ReserializeAssetsAndMetadata);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "Rebuilt seven canonical pure weapon-stat PerkData assets. " +
                "Legacy hash IDs were consumed only by the Editor converter.");
        }

        private static PerkData Rebuild(PerkMigration migration)
        {
            PerkData perk = AssetDatabase.LoadAssetAtPath<PerkData>(
                migration.AssetPath);
            if (perk == null)
            {
                perk = ScriptableObject.CreateInstance<PerkData>();
                AssetDatabase.CreateAsset(perk, migration.AssetPath);
            }

            perk.ID = migration.ContentId;
            perk.Title = migration.Title;
            perk.hasLocalization = true;
            perk.poolWeight = 1f;
            perk.Dependencies = Array.Empty<AstralShift.HellMaiden.Data.Cards.CardData>();

            var serialized = new SerializedObject(perk);
            serialized.FindProperty("Description").stringValue = migration.Description;
            serialized.FindProperty("TitleKey").stringValue = migration.TitleKey;
            serialized.FindProperty("DescriptionKey").stringValue = migration.DescriptionKey;
            string iconPath = AssetDatabase.GUIDToAssetPath(migration.IconGuid);
            serialized.FindProperty("icon").objectReferenceValue =
                string.IsNullOrEmpty(iconPath)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var rarities = new PerkRarityModifiersData[migration.Rarities.Length];
            for (int i = 0; i < rarities.Length; i++)
            {
                var application = new PerkModifierApplication();
                application.Configure(
                    LegacyPerkModifierConverter.Convert(
                        migration.LegacyModifierId,
                        migration.Increments[i]),
                    PerkApplicationDomain.WeaponStats,
                    migration.DescriptionToken);
                rarities[i] = new PerkRarityModifiersData();
                rarities[i].Configure(
                    migration.Rarities[i],
                    new[] { application });
            }

            perk.ConfigureNativeModifiers(rarities);
            EditorUtility.SetDirty(perk);
            return perk;
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

        private readonly struct PerkMigration
        {
            public PerkMigration(
                string assetPath,
                uint contentId,
                string title,
                string description,
                string titleKey,
                string descriptionKey,
                string iconGuid,
                string descriptionToken,
                uint legacyModifierId,
                PerkRarity[] rarities,
                float[] increments)
            {
                if (rarities == null || increments == null ||
                    rarities.Length != increments.Length)
                {
                    throw new ArgumentException(
                        "Perk rarity and increment arrays must have equal length.");
                }

                AssetPath = assetPath;
                ContentId = contentId;
                Title = title;
                Description = description;
                TitleKey = titleKey;
                DescriptionKey = descriptionKey;
                IconGuid = iconGuid;
                DescriptionToken = descriptionToken;
                LegacyModifierId = legacyModifierId;
                Rarities = rarities;
                Increments = increments;
            }

            public string AssetPath { get; }
            public uint ContentId { get; }
            public string Title { get; }
            public string Description { get; }
            public string TitleKey { get; }
            public string DescriptionKey { get; }
            public string IconGuid { get; }
            public string DescriptionToken { get; }
            public uint LegacyModifierId { get; }
            public PerkRarity[] Rarities { get; }
            public float[] Increments { get; }
        }
    }
}
