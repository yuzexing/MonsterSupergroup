using System.Linq;
using AstralShift.HellMaiden.AI.Enemy;
using Mirror;
using MonsterSupergroup.NetworkCombat.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class LimboImpTests
    {
        [Test]
        public void ReferenceImpMatchesRecoveredAttackWithoutChangingNormalPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LimboImpAssets.EnemyPath);
            var original = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabVariantMigration.ImpPath);
            var controller = prefab.GetComponent<EnemyController>(); var attack = prefab.GetComponent<EnemyProjectileAttack>();
            attack.enemyAnimator = controller.enemyAnimator;
            Assert.That(attack.WarningTime, Is.EqualTo(.85f).Within(.00001));
            Assert.That(attack.AttackTime, Is.EqualTo(.29f).Within(.00001));
            Assert.That(attack.RecoveryTime, Is.EqualTo(.43f).Within(.00001));
            Assert.That(controller.attackCooldown, Is.EqualTo(2)); Assert.That(controller.attackDistance, Is.EqualTo(10));
            Assert.That(attack.bulletPrefab.speed, Is.EqualTo(5)); Assert.That(attack.bulletPrefab.duration, Is.EqualTo(3));
            Assert.That(attack.bulletPrefab.bulletHasTimeOut, Is.False); Assert.That(attack.bulletPrefab.pierce, Is.EqualTo(1));
            Assert.That(attack.bulletPrefab.GetComponentInChildren<CircleCollider2D>(true).radius, Is.EqualTo(.37f));
            Assert.That(prefab.GetComponent<NetworkIdentity>().assetId, Is.Not.EqualTo(original.GetComponent<NetworkIdentity>().assetId));
            Assert.That(original.GetComponent<EnemyProjectileAttack>().bulletPrefab.speed, Is.EqualTo(6));
            Assert.That(original.GetComponent<EnemyController>().stats.BaseHealth, Is.EqualTo(12));
        }

        [Test]
        public void PreviewKeepsSourceBudgetsAndStopsBeforeElite()
        {
            var rules = AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot + "/ImpValidation.asset");
            Assert.That(rules.TryCapture(out var p, out var error), Is.True, error);
            Assert.That(p.Reference.ReadinessError(), Is.Null);
            var clips = p.Reference.Clips.Where(c => c.Start < p.Reference.EndTime).ToArray();
            Assert.That(p.Reference.EndTime, Is.EqualTo(105)); Assert.That(clips.Length, Is.EqualTo(2));
            Assert.That(clips[0].End, Is.EqualTo(136)); Assert.That(clips[0].Count, Is.EqualTo(250));
            Assert.That(clips[1].Start, Is.EqualTo(60)); Assert.That(clips[1].End, Is.EqualTo(105)); Assert.That(clips[1].Count, Is.EqualTo(15));
            Assert.That(clips[1].Stats.BaseHealth, Is.EqualTo(50)); Assert.That(p.Reference.SourceDuration, Is.EqualTo(841.5766649882).Within(.0001));
            var full = AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot + "/Full.asset");
            Assert.That(full.TryCapture(out var f, out error), Is.True, error); Assert.That(f.Reference.ReadinessError(), Is.Not.Null);
        }

        [Test]
        public void ScreenExitIsRequiredAfterMinimumDurationAndUsesCapturedTargetViewport()
        {
            var launch = new EnemyProjectileLaunch { Origin = Vector3.zero, Direction = Vector2.right, Speed = 5, Lifetime = 3, FiredAt = 10 };
            var view = new Bounds(Vector3.zero, new Vector3(50, 24, 1));
            Assert.That(launch.OutsideViewExpired(13, new Bounds(Vector3.zero, Vector3.one)), Is.False);
            Assert.That(launch.OutsideViewExpired(14, view), Is.False);
            Assert.That(launch.PositionAt(14), Is.EqualTo(new Vector3(20, 0, 0)));
            Assert.That(launch.OutsideViewExpired(16, view), Is.True);
            view.center = new Vector3(30, 0, 0);
            Assert.That(launch.OutsideViewExpired(16, view), Is.False);
        }

        [Test]
        public void ReferenceLaunchRoundTripsClockModeTargetAndHitClaim()
        {
            var launch = new EnemyProjectileLaunch { Key = new EnemyProjectileKey { EnemyEntityId = 8, ActionId = 999 },
                AssignmentEpoch = 2, EnemyPrefabAssetId = 20, Direction = Vector2.right, Speed = 5, Lifetime = 3, Damage = 70,
                ExpiryMode = EnemyProjectileExpiryMode.ReferenceOutsideView, FiredAt = 43.5, ViewTargetPlayerId = 8192 };
            Assert.That(launch.IsValid, Is.True);
            var packet = new EnemyAttackPresentationBatch { ProjectileLaunches = new[] { launch },
                ProjectileTerminations = new[] { new EnemyProjectileTermination { Key = launch.Key, Reason = EnemyProjectileEndReason.Hit, TargetPlayerId = 8192 } } };
            var writer = new NetworkWriter(); writer.Write(packet);
            var restored = new NetworkReader(writer.ToArraySegment()).Read<EnemyAttackPresentationBatch>();
            Assert.That(restored.ProjectileLaunches[0].ExpiryMode, Is.EqualTo(launch.ExpiryMode));
            Assert.That(restored.ProjectileLaunches[0].FiredAt, Is.EqualTo(43.5));
            Assert.That(restored.ProjectileLaunches[0].ViewTargetPlayerId, Is.EqualTo(8192));
            Assert.That(restored.ProjectileTerminations[0].TargetPlayerId, Is.EqualTo(8192));
            var checkpoint = new NetworkWriter(); checkpoint.Write(launch.Checkpoint);
            Assert.That(writer.Position, Is.EqualTo(CombatBandwidthEstimator.EstimateEnemyProjectilePayloadBytes(packet, checkpoint.Position)));
        }
    }
}
