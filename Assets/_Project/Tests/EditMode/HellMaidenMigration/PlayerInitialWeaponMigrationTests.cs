using System;
using System.IO;
using System.Reflection;
using AstralShift.HellMaiden.Player;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.HellMaidenMigration.Editor;
using MonsterSupergroup.NetworkCombat.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.HellMaidenMigration.Tests
{
    public sealed class PlayerInitialWeaponMigrationTests
    {
        [TestCase(false, 6u)]
        [TestCase(false, 1u)]
        [TestCase(false, 0u)]
        [TestCase(true, 6u)]
        [TestCase(true, 1u)]
        [TestCase(true, 0u)]
        public void FocusedMigrationPreservesExistingChoiceAndIsIdempotent(bool networkMigration, uint existingChoice)
        {
            string source = DanteNativeGasMigration.NetworkPlayerPrefabPath;
            byte[] original = File.ReadAllBytes(source);
            string folder = "Assets/__WeaponMigrationTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            string copy = folder + "/Player.prefab";
            try
            {
                if (existingChoice == 0)
                {
                    var fresh = new GameObject("Player before build migration");
                    try
                    {
                        fresh.AddComponent<PlayerMovement>();
                        fresh.AddComponent<CombatantBehaviour>();
                        PrefabUtility.SaveAsPrefabAsset(fresh, copy);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(fresh); }
                }
                else
                {
                    Assert.That(AssetDatabase.CopyAsset(source, copy), Is.True);
                    Edit(copy, root => root.GetComponent<PlayerBuildRuntime>().ConfigureInitialWeapon(existingChoice));
                }
                for (int pass = 0; pass < 2; pass++)
                {
                    if (networkMigration)
                        Edit(copy, root => Invoke(typeof(PlayerRuntimeCombatPrefabMigrator), "ConfigureNetworkPlayer", root));
                    else
                        Invoke(typeof(DanteNativeGasMigration), "EnsurePlayerBuildRuntime", copy);

                    GameObject result = AssetDatabase.LoadAssetAtPath<GameObject>(copy);
                    PlayerBuildRuntime[] builds = result.GetComponentsInChildren<PlayerBuildRuntime>(true);
                    Assert.That(builds, Has.Length.EqualTo(1), $"pass {pass}");
                    Assert.That(builds[0].InitialWeaponId, Is.EqualTo(existingChoice == 0 ? 2u : existingChoice),
                        $"pass {pass}: only a newly added component receives the migration default");
                }
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
                Assert.That(File.ReadAllBytes(source), Is.EqualTo(original), "The real player prefab must remain unchanged.");
            }
        }

        private static void Edit(string path, Action<GameObject> edit)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                edit(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void Invoke(Type type, string method, object argument)
        {
            MethodInfo target = type.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(target, Is.Not.Null);
            target.Invoke(null, new[] { argument });
        }
    }
}
