using System.IO;
using System.Linq;
using AstralShift.HellMaiden.AI.Enemy;
using Mirror;
using MonsterSupergroup.NetworkCombat.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class LustSinnerVariantTests
    {
        private static GameObject Load() => AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabVariantMigration.LustSinnerPath);

        [Test]
        public void DirectVariantHasSourceValuesAndInheritedLife()
        {
            EnemyPrefabVariantMigration.ValidateLustSinner();
            var root = Load();
            var parent = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabVariantMigration.BasePath);
            Assert.That(PrefabUtility.GetPrefabAssetType(root), Is.EqualTo(PrefabAssetType.Variant));
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(root), Is.SameAs(parent));
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(root.GetComponent<EnemyCombatantBinding>()), Is.SameAs(parent.GetComponent<EnemyCombatantBinding>()));
            Assert.That(root.GetComponentsInChildren<EnemyController>(true).Length, Is.EqualTo(1));
            Assert.That(root.GetComponentsInChildren<NetworkIdentity>(true).Length, Is.EqualTo(1));
            Assert.That(root.GetComponent<EnemyContactDamage>().ContactEnabled, Is.False);
            var controller = root.GetComponent<EnemyController>();
            var stats = new SerializedObject(controller);
            Assert.That(stats.FindProperty("stats.baseStats.hp").intValue, Is.EqualTo(15));
            Assert.That(stats.FindProperty("stats.baseStats.damage").intValue, Is.EqualTo(5));
            Assert.That(stats.FindProperty("stats.baseStats.speed").floatValue, Is.EqualTo(4));
            Assert.That(stats.FindProperty("stats.baseStats.xp").floatValue, Is.EqualTo(6));
            Assert.That(controller.attackDistance, Is.EqualTo(3));
            Assert.That(controller.attackCooldown, Is.EqualTo(.5f));
            var attack = root.GetComponent<EnemyAttackMelee>();
            attack.enemyAnimator = controller.enemyAnimator;
            Assert.That(attack.WarningTime, Is.EqualTo(.5142857f).Within(.000001));
            Assert.That(attack.AttackTime, Is.EqualTo(.10714286f).Within(.000001));
            Assert.That(attack.RecoveryTime, Is.EqualTo(.4357143f).Within(.000001));
        }

        [Test]
        public void ImportedRenderResourcesAndWarningGeometryResolve()
        {
            var root = Load();
            var attack = root.GetComponent<EnemyAttackMelee>().attackPrefab;
            Assert.That(root.GetComponent<EnemyController>().enemyAnimator.GetComponent<SpriteRenderer>().sharedMaterial.IsKeywordEnabled("HITEFFECT_ON"), Is.True);
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true).Where(r => r.GetType().Name == "RenderAs2D"))
                Assert.That(renderer.sharedMaterial.shader.name, Is.EqualTo("2D/RenderAs2D-Flattening"), "The native compositor cannot use a Sprite shader.");
            foreach (var go in new[] {root, attack.gameObject})
            {
                Assert.That(go.GetComponentsInChildren<Component>(true).Any(c => c == null), Is.False);
                foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
                    foreach (var material in renderer.sharedMaterials)
                    {
                        Assert.That(material, Is.Not.Null, renderer.name);
                        Assert.That(material.shader, Is.Not.Null, material.name);
                        Assert.That(material.shader.name, Does.Not.Contain("InternalError"));
                    }
                foreach (var renderer in go.GetComponentsInChildren<SpriteRenderer>(true))
                    Assert.That(renderer.sprite, Is.Not.Null, renderer.name);
            }
            var polygon = attack.GetComponentInChildren<PolygonCollider2D>(true);
            Assert.That(polygon.isTrigger, Is.True);
            Assert.That(polygon.GetPath(0).Length, Is.EqualTo(6));
            Assert.That(polygon.GetPath(0)[0], Is.EqualTo(new Vector2(1.3611355f, 1.1078366f)));
            var warning = new SerializedObject(attack.attackWarning);
            Assert.That(warning.FindProperty("warningStart._Clip").objectReferenceValue, Is.Null);
            Assert.That(warning.FindProperty("warningEnd._Clip").objectReferenceValue, Is.Null);
        }

        [Test]
        public void RepeatedMigrationAndExistingToolsPreserveAllAuthoredAssets()
        {
            string[] paths = {EnemyPrefabVariantMigration.BasePath, EnemyPrefabVariantMigration.SkeletonPath,
                EnemyPrefabVariantMigration.ExamplePath, EnemyPrefabVariantMigration.LustSinnerPath,
                EnemyPrefabVariantMigration.LustWarningPath, NetworkCombatSetupUtility.BootScenePath,
                NetworkCombatSetupUtility.SandboxScenePath, NetworkCombatSetupUtility.GameplayScenePath};
            var before = paths.ToDictionary(p => p, File.ReadAllBytes);
            EnemyPrefabVariantMigration.MigrateLustSinner();
            EnemyPrefabVariantMigration.MigrateLustSinner();
            EnemySimulationPrefabMigrator.Migrate();
            foreach (var path in paths) Assert.That(File.ReadAllBytes(path), Is.EqualTo(before[path]), path);
        }

        [Test]
        public void RepairMissingAnimationRetainsCustomStatsAndValidAnimations()
        {
            string path = EnemyPrefabVariantMigration.LustSinnerPath;
            var before = File.ReadAllBytes(path);
            try
            {
                var root = Load();
                var controller = new SerializedObject(root.GetComponent<EnemyController>());
                controller.FindProperty("stats.baseStats.hp").intValue = 31;
                controller.ApplyModifiedPropertiesWithoutUndo();
                var animator = new SerializedObject(root.GetComponent<EnemyController>().enemyAnimator);
                var preserved = animator.FindProperty("moveLeftUp._Clip").objectReferenceValue;
                animator.FindProperty("attackRightDown._Clip").objectReferenceValue = null;
                animator.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SavePrefabAsset(root);
                EnemyPrefabVariantMigration.MigrateLustSinner();
                Assert.That(new SerializedObject(Load().GetComponent<EnemyController>()).FindProperty("stats.baseStats.hp").intValue, Is.EqualTo(31));
                animator = new SerializedObject(Load().GetComponent<EnemyController>().enemyAnimator);
                Assert.That(animator.FindProperty("attackRightDown._Clip").objectReferenceValue, Is.Not.Null);
                Assert.That(animator.FindProperty("moveLeftUp._Clip").objectReferenceValue, Is.SameAs(preserved));
            }
            finally { File.WriteAllBytes(path, before); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport); }
        }
    }
}
