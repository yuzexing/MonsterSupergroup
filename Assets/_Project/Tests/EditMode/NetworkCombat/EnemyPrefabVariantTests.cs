using System;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.AI.Enemy;
using Mirror;
using MonsterSupergroup.NetworkCombat.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class EnemyPrefabVariantTests
    {
        private static GameObject Load(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);

        [Test]
        public void MigrationPreservesUnsavedSceneEdits()
        {
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                EditorSceneManager.MarkSceneDirty(scene);
                var error = Assert.Throws<InvalidOperationException>(() => EnemyPrefabVariantMigration.Migrate());
                Assert.That(error.Message, Does.Contain("Save open scene edits"));
                Assert.That(scene.isLoaded && scene.isDirty, Is.True);
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (setup.Any(s => s.isLoaded && s.isActive && !string.IsNullOrEmpty(s.path)))
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
        }

        [Test]
        public void DirectVariantsHaveInheritedCoreAndIndependentSpawnIdentities()
        {
            EnemyPrefabVariantMigration.Validate();
            var parent = Load(EnemyPrefabVariantMigration.BasePath);
            var child = Load(EnemyPrefabVariantMigration.SkeletonPath);
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(child.GetComponent<EnemyController>()),
                Is.SameAs(parent.GetComponent<EnemyController>()));
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(child.GetComponent<EnemyCombatantBinding>()),
                Is.SameAs(parent.GetComponent<EnemyCombatantBinding>()));
            Assert.That(parent.GetComponent<ThornsEnemyAttack>(), Is.Null);
            Assert.That(parent.GetComponent<EnemyController>().attackScript, Is.Null);
            Assert.That(parent.GetComponent<EnemyContactDamage>().ContactEnabled, Is.True);
            Assert.That(child.GetComponent<EnemyContactDamage>().ContactEnabled, Is.False);
            Assert.That(child.GetComponent<EnemyController>().attackScript, Is.SameAs(child.GetComponent<EnemyAttackMelee>()));
            Assert.That(parent.GetComponent<NetworkIdentity>().assetId, Is.EqualTo(2503929215u));
            Assert.That(child.GetComponent<NetworkIdentity>().assetId, Is.EqualTo(2821354368u));
        }

        [Test]
        public void UnoverriddenParentPropertyPropagatesWithoutChangingChildAttributes()
        {
            var parent = Load(EnemyPrefabVariantMigration.BasePath);
            var controller = parent.GetComponent<EnemyController>();
            float original = controller.obstacleDetectionCastDistance;
            try
            {
                var serialized = new SerializedObject(controller);
                serialized.FindProperty("obstacleDetectionCastDistance").floatValue = original + .25f;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SavePrefabAsset(parent);
                var child = Load(EnemyPrefabVariantMigration.SkeletonPath).GetComponent<EnemyController>();
                Assert.That(child.obstacleDetectionCastDistance, Is.EqualTo(original + .25f));
                Assert.That(new SerializedObject(child).FindProperty("stats.baseStats.hp").intValue, Is.EqualTo(2000));
                Assert.That(new SerializedObject(controller).FindProperty("stats.baseStats.hp").intValue, Is.EqualTo(100));
            }
            finally
            {
                var serialized = new SerializedObject(controller);
                serialized.FindProperty("obstacleDetectionCastDistance").floatValue = original;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SavePrefabAsset(parent);
            }
        }

        [Test]
        public void SkeletonRetainsAuthoredTimingsPhysicsAndAnimationBindings()
        {
            var root = Load(EnemyPrefabVariantMigration.SkeletonPath);
            var controller = root.GetComponent<EnemyController>();
            var melee = root.GetComponent<EnemyAttackMelee>();
            melee.enemyAnimator = controller.enemyAnimator;
            Assert.That(melee.WarningTime, Is.EqualTo(.57f).Within(.0001));
            Assert.That(melee.AttackTime, Is.EqualTo(.08f).Within(.0001));
            Assert.That(melee.RecoveryTime, Is.EqualTo(.29f).Within(.0001));
            Assert.That(controller.attackCooldown, Is.EqualTo(.5f));
            Assert.That(controller.attackDistance, Is.EqualTo(1.75f));
            Assert.That(root.GetComponent<Rigidbody2D>().mass, Is.EqualTo(20));
            Assert.That(root.GetComponent<Rigidbody2D>().linearDamping, Is.EqualTo(10));
            foreach (var clip in new[] { controller.enemyAnimator.MoveLeftDown.Clip,
                controller.enemyAnimator.MoveRightDown.Clip, controller.enemyAnimator.AttackWarningLeftDown.Clip,
                controller.enemyAnimator.AttackLeftDown.Clip, controller.enemyAnimator.RecoveryLeftDown.Clip })
                foreach (var binding in AnimationUtility.GetCurveBindings(clip).Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip)))
                    Assert.That(AnimationUtility.GetAnimatedObject(controller.enemyAnimator.animancer.gameObject, binding),
                        Is.Not.Null, clip.name + ":" + binding.path + ":" + binding.propertyName);
            var example = Load(EnemyPrefabVariantMigration.ExamplePath);
            Assert.That(new SerializedObject(example.GetComponent<EnemyController>()).FindProperty("stats.baseStats.hp").intValue,
                Is.EqualTo(2400));
            Assert.That(example.transform.Find("Sprite").localScale,
                Is.EqualTo(root.transform.Find("Sprite").localScale * 1.1f));
        }

        [Test]
        public void ExistingSetupEntrypointsAreIdempotentAndPreserveAuthoredOverrides()
        {
            string[] paths = { EnemyPrefabVariantMigration.BasePath, EnemyPrefabVariantMigration.SkeletonPath,
                EnemyPrefabVariantMigration.ExamplePath, NetworkCombatSetupUtility.BootScenePath,
                NetworkCombatSetupUtility.GameplayScenePath, NetworkCombatSetupUtility.SandboxScenePath };
            var original = paths.ToDictionary(p => p, File.ReadAllBytes);
            NetworkCombatSetupUtility.BuildBootGameplayAssets();
            NetworkCombatSetupUtility.BuildSandboxAssets();
            EnemySimulationPrefabMigrator.Migrate();
            foreach (string path in paths) Assert.That(File.ReadAllBytes(path), Is.EqualTo(original[path]), path);
            EnemyPrefabVariantMigration.Validate();
        }

        [Test]
        public void MissingRequiredBindingRepairsWithoutResettingAnAuthoredStat()
        {
            string path = EnemyPrefabVariantMigration.ExamplePath;
            var saved = File.ReadAllBytes(path);
            try
            {
                var root = Load(path);
                var serialized = new SerializedObject(root.GetComponent<EnemyController>());
                serialized.FindProperty("enemyAnimator").objectReferenceValue = null;
                serialized.FindProperty("stats.baseStats.hp").intValue = 2500;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SavePrefabAsset(root);
                EnemySimulationPrefabMigrator.Migrate();
                var controller = Load(path).GetComponent<EnemyController>();
                Assert.That(controller.enemyAnimator, Is.Not.Null);
                Assert.That(new SerializedObject(controller).FindProperty("stats.baseStats.hp").intValue, Is.EqualTo(2500));
                Assert.That(EnemyPrefabVariantMigration.IsDirectVariant(path, EnemyPrefabVariantMigration.SkeletonPath), Is.True);
            }
            finally
            {
                File.WriteAllBytes(path, saved);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            EnemyPrefabVariantMigration.Migrate();
        }
    }
}
