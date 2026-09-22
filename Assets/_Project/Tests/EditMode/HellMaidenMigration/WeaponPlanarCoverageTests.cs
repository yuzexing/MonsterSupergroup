using System.IO;
using System.Linq;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.HellMaidenMigration.Editor;
using MonsterSupergroup.NetworkCombat.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.HellMaidenMigration.Tests
{
    public sealed class WeaponPlanarCoverageTests
    {
        [Test]
        public void ProductionWeaponDependenciesUseFaithfulCopiesAndKeepSummonBodies()
        {
            int effects = 0, bodies = 0;
            var paths = ManualPlayFixAssets.WeaponPrefabPaths();
            Assert.That(paths.Length, Is.GreaterThan(15));
            foreach (string path in paths)
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (!ManualPlayFixAssets.IsEffectRenderer(root, renderer))
                    {
                        Assert.That(GameplayPlanarEffect.UsesPlanarMaterial(renderer), Is.False, path + "/" + renderer.name);
                        bodies++; continue;
                    }
                    foreach (var material in renderer.sharedMaterials.Where(m => m != null))
                    {
                        string adaptedPath = AssetDatabase.GetAssetPath(material);
                        string sourcePath = AssetDatabase.GUIDToAssetPath(Path.GetFileNameWithoutExtension(adaptedPath));
                        Assert.That(material.HasProperty("_GameplayPlanar"), Is.True, path + "/" + renderer.name);
                        Assert.That(PlanarMaterialValidation.IsOriginalOrFaithfulPlanarCopy(material, Path.GetDirectoryName(sourcePath).Replace('\\', '/')),
                            Is.True, adaptedPath);
                        effects++;
                    }
                    if (renderer.sharedMaterials.Any(m => m != null))
                        Assert.That(renderer.GetComponentInParent<GameplayPlanarEffect>(true), Is.Not.Null, path);
                }
            }
            Assert.That(bodies, Is.EqualTo(9), "Three cocoon/body/feet-shadow renderers per summon variant remain unchanged.");
            Assert.That(effects, Is.GreaterThan(100));
            TestContext.WriteLine($"prefabs={paths.Length}; effect material slots={effects}; preserved body renderers={bodies}");
        }

        [Test]
        public void ReapplyingMigrationKeepsPrefabsAndReviewedMaterialValues()
        {
            var paths = ManualPlayFixAssets.WeaponPrefabPaths();
            var before = paths.ToDictionary(p => p, File.ReadAllBytes);
            var material = paths.SelectMany(p => AssetDatabase.LoadAssetAtPath<GameObject>(p).GetComponentsInChildren<Renderer>(true))
                .SelectMany(r => r.sharedMaterials).First(m => m != null && m.HasProperty("_GameplayPlanar") && m.HasProperty("_Color"));
            var original = material.GetColor("_Color");
            var reviewed = new Color(.23f, .51f, .79f, .81f);
            try
            {
                material.SetColor("_Color", reviewed); EditorUtility.SetDirty(material); AssetDatabase.SaveAssets();
                ManualPlayFixAssets.Apply();
                Assert.That(Vector4.Distance(material.GetColor("_Color"), reviewed), Is.LessThan(.000001f), "Only serialization precision is tolerated.");
                foreach (string path in paths) Assert.That(File.ReadAllBytes(path), Is.EqualTo(before[path]), path);
            }
            finally
            {
                material.SetColor("_Color", original); EditorUtility.SetDirty(material); AssetDatabase.SaveAssets();
            }
        }
    }

    internal static class PlanarTestMaterials
    {
        // Preserve the old identity/shader assertions after verifying every adapted property.
        internal static Material Source(Material material, string sourceFolder)
        {
            if (!material.HasProperty("_GameplayPlanar")) return material;
            Assert.That(PlanarMaterialValidation.IsOriginalOrFaithfulPlanarCopy(material, sourceFolder), Is.True, AssetDatabase.GetAssetPath(material));
            return AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(material))));
        }
    }
}
