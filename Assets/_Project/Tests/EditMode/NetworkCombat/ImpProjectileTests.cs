using System.Linq;
using AstralShift.HellMaiden.AI.Enemy;
using Mirror;
using MonsterSupergroup.NetworkCombat.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class ImpProjectileTests
    {
        [Test]
        public void ImpIsDirectVariantWithSourceStatsAndLocalBullet()
        {
            EnemyPrefabVariantMigration.ValidateImp();
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabVariantMigration.ImpPath);
            var c = root.GetComponent<EnemyController>(); var a = root.GetComponent<EnemyProjectileAttack>();
            Assert.That(c.stats.BaseHealth, Is.EqualTo(12)); Assert.That(c.stats.BaseDamage, Is.EqualTo(5));
            Assert.That(c.stats.BaseSpeed, Is.EqualTo(2)); Assert.That(c.stats.BaseXP, Is.EqualTo(6));
            Assert.That(c.attackDistance, Is.EqualTo(10)); Assert.That(c.attackCooldown, Is.EqualTo(2));
            Assert.That(root.GetComponent<EnemyContactDamage>().ContactEnabled, Is.False);
            Assert.That(root.GetComponent<NetworkEnemySimulationAgent>().ProductMovementOnly, Is.False);
            Assert.That(a.bulletPrefab.speed, Is.EqualTo(6)); Assert.That(a.bulletPrefab.duration, Is.EqualTo(5));
            Assert.That(a.bulletPrefab.GetComponentsInChildren<NetworkIdentity>(true), Is.Empty);
            a.enemyAnimator = c.enemyAnimator;
            Assert.That(a.WarningTime, Is.EqualTo(.85f).Within(.0001));
            Assert.That(a.AttackTime, Is.EqualTo(.29f).Within(.0001));
            Assert.That(a.RecoveryTime, Is.EqualTo(.43f).Within(.0001));
        }

        [Test]
        public void SourceAnimationCurvesAndResourcesResolve()
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabVariantMigration.ImpPath);
            var animator = root.GetComponent<EnemyController>().enemyAnimator; var so = new SerializedObject(animator);
            foreach (string prefix in new[] { "move", "attackWarning", "attack", "recovery", "hurt", "dead" })
                foreach (string side in new[] { "LeftUp", "LeftDown", "RightUp", "RightDown" })
                {
                    var clip = (AnimationClip)so.FindProperty(prefix + side + "._Clip").objectReferenceValue;
                    Assert.That(clip, Is.Not.Null);
                    foreach (var curve in AnimationUtility.GetCurveBindings(clip).Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip)))
                        Assert.That(AnimationUtility.GetAnimatedObject(animator.gameObject, curve), Is.Not.Null, clip.name + ":" + curve.path);
                }
            foreach (var go in new[] { root, root.GetComponent<EnemyProjectileAttack>().bulletPrefab.gameObject })
                foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
                    foreach (var material in renderer.sharedMaterials)
                    { Assert.That(material, Is.Not.Null); Assert.That(material.shader.name, Does.Not.Contain("InternalError")); }
        }

        [Test]
        public void LaunchAndTerminationRoundTripIncludeUniqueKeyAndProgress()
        {
            var launch = new EnemyProjectileLaunch { Key = new EnemyProjectileKey { EnemyEntityId = 4, ActionId = 0x100000003, ProjectileIndex = 0 },
                AssignmentEpoch = 2, EnemyPrefabAssetId = 7, Origin = new Vector3(3, 2, 1), Direction = Vector2.left, Speed = 6, Lifetime = 5, Damage = 5 };
            launch.Checkpoint.Movement.Runtime.Action.ProjectileDirection = Vector2.left;
            launch.Checkpoint.Movement.Runtime.Action.ProjectileEmitted = true;
            var terminal = new EnemyProjectileTermination { Key = launch.Key, Reason = EnemyProjectileEndReason.Hit };
            var packet = new EnemyAttackPresentationBatch { ProjectileLaunches = new[] { launch }, ProjectileTerminations = new[] { terminal } };
            var writer = new NetworkWriter(); writer.Write(packet);
            var copy = new NetworkReader(writer.ToArraySegment()).Read<EnemyAttackPresentationBatch>();
            var checkpointWriter = new NetworkWriter(); checkpointWriter.Write(launch.Checkpoint);
            Assert.That(writer.Position, Is.EqualTo(CombatBandwidthEstimator.EstimateEnemyProjectilePayloadBytes(packet, checkpointWriter.Position)));
            Assert.That(copy.ProjectileLaunches.Single().Key, Is.EqualTo(launch.Key));
            Assert.That(copy.ProjectileLaunches[0].Origin, Is.EqualTo(launch.Origin));
            Assert.That(copy.ProjectileLaunches[0].Checkpoint.Movement.Runtime.Action.ProjectileEmitted, Is.True);
            Assert.That(copy.ProjectileTerminations.Single().Reason, Is.EqualTo(EnemyProjectileEndReason.Hit));
        }

        [Test]
        public void HandoffKeepsConfirmedEmissionDespiteNewerUnconfirmedCheckpoint()
        {
            var registry = new ServerEnemySimulationRegistry(); registry.RegisterEnemy(1, Vector2.zero, 0);
            var assigned = registry.AssignClientOwner(1, 5, 5);
            var pose = new EnemySimulationSnapshot { EnemyEntityId = 1, AssignmentEpoch = assigned.Epoch, SampleNetworkTime = 1 };
            pose.Runtime.Action.ActionId = ((ulong)assigned.Epoch << 32) | 1;
            var launch = new EnemyProjectileLaunch { Key = new EnemyProjectileKey { EnemyEntityId = 1, ActionId = pose.Runtime.Action.ActionId }, Checkpoint = new EnemySimulationCheckpoint { Movement = pose } };
            registry.ConfirmProjectileLaunch(launch);
            pose.SampleNetworkTime = 2; pose.Runtime.Action.ProjectileEmitted = false;
            registry.RecordCheckpoint(new EnemySimulationCheckpoint { Movement = pose });
            registry.AssignClientOwner(1, 6, 6);
            Assert.That(registry.TryGetLatestSnapshot(1, out var restored), Is.True);
            Assert.That(restored.Runtime.Action.ProjectileEmitted, Is.True);
            Assert.That(restored.Runtime.Action.ActionId, Is.EqualTo(launch.Key.ActionId));
        }

        [Test]
        public void HistoryDistinguishesEnemiesAndExpiresAcrossRuns()
        {
            var h = new EnemyProjectileHistory(2, 120);
            var a = new EnemyProjectileKey { EnemyEntityId = 1, ActionId = 1 }; var b = a; b.EnemyEntityId = 2;
            Assert.That(h.Add(a, 0), Is.True); Assert.That(h.Add(a, 1), Is.False); Assert.That(h.Add(b, 1), Is.True);
            Assert.That(h.Contains(a, 119), Is.True); Assert.That(h.Contains(a, 121), Is.False);
            h.Clear(); Assert.That(h.Contains(b, 121), Is.False);
        }
    }
}
