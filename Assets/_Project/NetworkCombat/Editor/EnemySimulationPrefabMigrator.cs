using System;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static class EnemySimulationPrefabMigrator
    {
        public const string SkeletonNetworkPrefabPath =
            "Assets/_Project/Content/NetworkCombat/NetworkEnemySkeleton.prefab";

        [MenuItem("Monster Supergroup/Network Combat/Migrate Enemy Simulation Prefabs")]
        public static void Migrate() => EnemyPrefabVariantMigration.Migrate();

        public static void MigrateBatch()
        {
            try
            {
                Migrate();
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(0);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                    return;
                }

                throw;
            }
        }

    }
}
