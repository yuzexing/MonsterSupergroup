using System;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static class EnemySimulationPrefabMigrator
    {
        public const string SkeletonNetworkPrefabPath =
            "Assets/_Project/Content/NetworkCombat/NetworkEnemySkeleton.prefab";


        public static void Migrate()
        {
            Debug.LogWarning("[ProjectTools] Compatibility alias; use migrate.enemy-variants -Apply.");
            EnemyPrefabVariantMigration.Migrate();
        }

        public static void MigrateBatch()
        {
            MonsterSupergroup.EditorTools.ProjectToolRunner.LegacyBatch("migrate.enemy-variants");
        }

    }
}
