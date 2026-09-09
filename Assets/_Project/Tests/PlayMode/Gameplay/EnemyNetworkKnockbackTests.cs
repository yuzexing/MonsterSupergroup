using System.Collections;
using System.Reflection;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player.Attacks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class EnemyNetworkKnockbackTests
    {
        private GameObject root;
        private KnockbackOnlyTestEnemy enemy;
        private EnemyDefaultMovement movement;
        private Rigidbody2D body;
        private KnockbackSettings preset;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Movement-only knockback test enemy");
            body = root.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            enemy = root.AddComponent<KnockbackOnlyTestEnemy>();
            enemy.rigidBody = body;
            enemy.stats = new EnemyStats();
            enemy.stats.Init(new EnemyStatsValues { Health = 100, KnockBackMultiplier = 1f, Speed = 2f });
            movement = root.AddComponent<EnemyDefaultMovement>();
            movement.Init(enemy);
            movement.SetRigidBody(body);
            movement.SetTransform(root.transform);
            enemy.defaultMovement = movement;
            enemy._currentMovementScript = movement;
            enemy.usesPathfinding = false;
            preset = ScriptableObject.CreateInstance<KnockbackSettings>();
            preset.distance = 2f;
            preset.speedMultiplier = 6f;
            preset.staggerTime = .6f;
            preset.speedCurve = AnimationCurve.Linear(0, 0, 1, 1);
        }

        [TearDown]
        public void TearDown()
        {
            enemy?.CancelNetworkKnockback();
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(preset);
        }

        [UnityTest]
        public IEnumerator MovementOnlyUsesExistingPhysicsWithoutConstructingAnAiStateMachineOrDamage()
        {
            Assert.That(enemy.StateMachine, Is.Null);
            Assert.That(enemy.TryApplyNetworkKnockback(Vector2.left, preset), Is.True);
            Assert.That(enemy.StateMachine, Is.Null);
            Assert.That(enemy.IsNetworkKnockbackActive, Is.True);
            Assert.That(movement.CanMove, Is.False);
            Vector2 destination = (Vector2)typeof(BaseEnemyMovement)
                .GetField("_endPoint", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(movement);
            // Source BruteForce adds multiplier 1; the preset's distance 2 therefore targets 4.
            Assert.That(destination.x, Is.EqualTo(4f).Within(.0001f));
            Assert.That(destination.y, Is.Zero.Within(.0001f));
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;
            Assert.That(body.position.x, Is.GreaterThan(0f));
            Assert.That(enemy.CurrentHealth, Is.EqualTo(100));
            Assert.That(enemy.DamageCalls, Is.Zero);
        }

        [UnityTest]
        public IEnumerator UnusedUninitializedPathComponentDoesNotInterruptTheAssignedMovementImpulse()
        {
            // Real enemy prefabs keep this component even when ResetMovementMethod does not initialize it.
            var unusedPathMovement = root.AddComponent<EnemyAILerpMovement>();
            enemy.aILerpMovement = unusedPathMovement;
            Assert.That(enemy.usesPathfinding, Is.False);
            Assert.That(unusedPathMovement.Rigidbody, Is.Null);
            Assert.That(unusedPathMovement.CanMove, Is.True);

            Assert.That(enemy.TryApplyNetworkKnockback(Vector2.left, preset), Is.True);
            Assert.That(enemy.IsNetworkKnockbackActive, Is.True);
            Assert.That(unusedPathMovement.CanMove, Is.True, "Unused path movement must not be stopped or initialized by an impulse.");
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;
            Assert.That(body.position.x, Is.GreaterThan(0f));

            enemy.CancelNetworkKnockback();
            Assert.That(enemy.IsNetworkKnockbackActive, Is.False);
            Assert.That(body.linearVelocity, Is.EqualTo(Vector2.zero));
            Assert.That(movement.CanMove, Is.True);
            Assert.That(unusedPathMovement.CanMove, Is.True);
            Assert.That(unusedPathMovement.Rigidbody, Is.Null);
            Assert.That(enemy.DamageCalls, Is.Zero);
        }

        [UnityTest]
        public IEnumerator AssignmentCancellationStopsCoroutineAndVelocityImmediatelyWithoutReplay()
        {
            Assert.That(enemy.TryApplyNetworkKnockback(Vector2.left, preset), Is.True);
            Assert.That(enemy.TryApplyNetworkKnockback(Vector2.left, preset), Is.False, "Source ignores overlapping knockback.");
            yield return new WaitForFixedUpdate();
            enemy.CancelNetworkKnockback();
            Vector2 stopped = body.position;
            Assert.That(enemy.IsNetworkKnockbackActive, Is.False);
            Assert.That(body.linearVelocity, Is.EqualTo(Vector2.zero));
            Assert.That(movement.CanMove, Is.True);
            yield return new WaitForSeconds(.25f);
            Assert.That(body.position, Is.EqualTo(stopped));
            Assert.That(body.linearVelocity, Is.EqualTo(Vector2.zero));
            Assert.That(enemy.DamageCalls, Is.Zero);
        }

        [UnityTest]
        public IEnumerator NaturalRecoveryPreservesTheSourceStaggerAndRestoresMovement()
        {
            Assert.That(enemy.TryApplyNetworkKnockback(Vector2.left, preset), Is.True);
            yield return new WaitForSeconds(.25f);
            Assert.That(enemy.IsNetworkKnockbackActive, Is.True, "The .6-second source stagger is still active after travel.");
            Assert.That(movement.CanMove, Is.False);
            float deadline = Time.realtimeSinceStartup + 2f;
            while (enemy.IsNetworkKnockbackActive && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(enemy.IsNetworkKnockbackActive, Is.False);
            Assert.That(movement.CanMove, Is.True);
            Assert.That(enemy.StateMachine, Is.Null);
            Assert.That(enemy.DamageCalls, Is.Zero);
        }

        [Test]
        public void ExistingMovementLockAndEnemyKnockbackMultiplierSurviveCancellation()
        {
            movement.StopMovement();
            enemy.stats.KnockBackMultiplier = .5f;
            Assert.That(enemy.TryApplyNetworkKnockback(Vector2.left, preset), Is.True);
            Vector2 destination = (Vector2)typeof(BaseEnemyMovement)
                .GetField("_endPoint", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(movement);
            Assert.That(destination.x, Is.EqualTo(2f), "2 preset distance * 2 BruteForce factor * .5 Enemy multiplier.");
            enemy.CancelNetworkKnockback();
            Assert.That(movement.CanMove, Is.False, "Cancelling a network impulse must not unlock a pre-existing movement lock.");
            Assert.That(body.linearVelocity, Is.EqualTo(Vector2.zero));
        }
    }

    public sealed class KnockbackOnlyTestEnemy : EnemyController
    {
        public int DamageCalls { get; private set; }
        public override bool IsAlive => true;
        public override bool IsDead => false;
        public override int CurrentHealth => 100;
        public override void Damage(Vector2 attackPosition, WeaponBehaviour weapon, DamageType type) { DamageCalls++; }
        public override void Damage(int damage, DamageType type) { DamageCalls++; }
    }
}
