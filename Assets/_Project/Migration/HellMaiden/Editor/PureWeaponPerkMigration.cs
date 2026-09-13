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
                "13877d7b4f7178b498eaec2d14951f3f",
                "PlayerDamage",
                LegacyPerkModifierConverter.LegacyWeaponDamageId,
                new[] { PerkRarity.Bronze, PerkRarity.Silver, PerkRarity.Gold },
                new[] { 0.05f, 0.07f, 0.10f }),
            new PerkMigration(
                "Assets/MonoBehaviour/AttackSpeedPerk.asset",
                3u,
                "db737da432e935044b6e9ac59f2a8aea",
                "PlayerAttackSpeed",
                LegacyPerkModifierConverter.LegacyWeaponSpeedId,
                new[] { PerkRarity.Bronze, PerkRarity.Silver, PerkRarity.Gold },
                new[] { 0.05f, 0.07f, 0.10f }),
            new PerkMigration(
                "Assets/MonoBehaviour/ExtraWeaponSizePerk.asset",
                22u,
                "7ae278d7d8146fd46aef6f73774a48e6",
                "WeaponSize",
                LegacyPerkModifierConverter.LegacyWeaponSizeId,
                new[] { PerkRarity.Bronze, PerkRarity.Silver, PerkRarity.Gold },
                new[] { 0.10f, 0.14f, 0.20f }),
            new PerkMigration(
                "Assets/MonoBehaviour/ExtraWeaponDurationPerk.asset",
                19u,
                "165a2185e76b84546bf8efcab5dbaec6",
                "WeaponDuration",
                LegacyPerkModifierConverter.LegacyWeaponDurationId,
                new[] { PerkRarity.Bronze, PerkRarity.Silver, PerkRarity.Gold },
                new[] { 0.10f, 0.14f, 0.20f }),
            new PerkMigration(
                "Assets/MonoBehaviour/ExtraCriteRatePerk.asset",
                18u,
                "2bf69392be039864f8ac3d44ca06d298",
                "CritRate",
                LegacyPerkModifierConverter.LegacyWeaponCritRateId,
                new[] { PerkRarity.Silver, PerkRarity.Gold },
                new[] { 0.03f, 0.06f }),
            new PerkMigration(
                "Assets/MonoBehaviour/ExtraCritMultiplierPerk.asset",
                17u,
                "e849de5503ed921459971e9ba2920403",
                "CritMultiplier",
                LegacyPerkModifierConverter.LegacyWeaponCritMultiplierId,
                new[] { PerkRarity.Bronze, PerkRarity.Silver, PerkRarity.Gold },
                new[] { 0.10f, 0.14f, 0.20f }),
            new PerkMigration(
                "Assets/MonoBehaviour/ExtraProjectilePerk.asset",
                28u,
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


        public static void Rebuild()
        {
            MonsterSupergroup.EditorTools.ProjectToolRunner.CheckLegacyMaintenance("rebuild.perks", "MonsterSupergroup.HellMaidenMigration.Editor.PureWeaponPerkMigration.Rebuild");
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
            perk.LocalizedTitle.TableReference = "MonsterContent";
            perk.LocalizedTitle.TableEntryReference = "perk." + migration.ContentId + ".name";
            perk.LocalizedDescription.TableReference = "MonsterContent";
            perk.LocalizedDescription.TableEntryReference = "perk." + migration.ContentId + ".description";
            perk.poolWeight = 1f;
            perk.Dependencies = Array.Empty<AstralShift.HellMaiden.Data.Cards.CardData>();

            var serialized = new SerializedObject(perk);
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
                IconGuid = iconGuid;
                DescriptionToken = descriptionToken;
                LegacyModifierId = legacyModifierId;
                Rarities = rarities;
                Increments = increments;
            }

            public string AssetPath { get; }
            public uint ContentId { get; }
            public string IconGuid { get; }
            public string DescriptionToken { get; }
            public uint LegacyModifierId { get; }
            public PerkRarity[] Rarities { get; }
            public float[] Increments { get; }
        }
    }
}
