using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Assets.Scripts.AstralShift.HellMaiden.Data;
using AstralShift.Helpers;
using AstralShift.HellMaiden.Player;
using MonsterSupergroup.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class PlayerDashMovementTests
    {
        private readonly List<UnityEngine.Object> objects = new List<UnityEngine.Object>();
        private readonly List<DashMotionParameters> committed = new List<DashMotionParameters>();
        private readonly List<ulong> starts = new List<ulong>();
        private readonly List<ulong> ends = new List<ulong>();
        private PlayerMovement player;
        private PlayerStats stats;
        private PlayerDashRuntime runtime;
        private CombatantBehaviour combatant;
        private Collider2D hitbox;
        private CircleCollider2D obstacle;
        private double now;
        private ulong nextUseId;

        [SetUp]
        public void SetUp()
        {
            now = 10d;
            nextUseId = 0;
            GameObject go = Create("Dash Movement Owner", false);
            combatant = go.AddComponent<CombatantBehaviour>();
            player = go.AddComponent<PlayerMovement>();
            player.Awake();
            player.body = go.AddComponent<Rigidbody2D>();
            player.body.gravityScale = 0f;
            player.body.constraints = RigidbodyConstraints2D.FreezeRotation;
            player.playerAnimator = go.AddComponent<DashMovementTestAnimator>();
            player.animator = player.playerAnimator;
            hitbox = go.AddComponent<BoxCollider2D>();
            hitbox.excludeLayers = 2;
            obstacle = go.AddComponent<CircleCollider2D>();
            obstacle.excludeLayers = 4;
            SetField("_hitboxCollider", hitbox);
            SetField("_obstacleCollider", obstacle);
            SetField("dashExclusionLayerMask", (LayerMask)8);
            SetField("dashCurve", AnimationCurve.Linear(0f, 1f, 1f, 1f));
            SetField("dashBufferTime", 0.1f);

            var database = ScriptableObject.CreateInstance<PlayerBaseStatsDatabase>();
            objects.Add(database);
            database.values = new PlayerStats.PlayerStatsValues
            {
                HP = 100, maxHP = 100, moveSpeed = 4f,
                dashCharges = 2, maxDashCharges = 2, dashDistance = 6f, dashSpeed = 20f, dashCooldown = 2.5f
            };
            stats = new PlayerStats { playerBaseStatsDatabase = database };
            SetField("playerStats", stats);
            player.EnsureRuntimeInitialized();
            player.ConfigureNetworkLifecycle();
            player.SetLocalOwnerBound(true);
            go.SetActive(true);
            runtime = new PlayerDashRuntime();
            player.BindDashRuntime(runtime, () => now, Commit);
            player.OnDashStart += () => starts.Add(player.CurrentDashUseId);
            player.OnDashEnd += () => ends.Add(player.CurrentDashUseId);
            player.SetDirection(Vector2.right);
        }

        [TearDown]
        public void TearDown()
        {
            if (player != null) player.UnbindDashRuntime();
            for (int index = objects.Count - 1; index >= 0; index--)
                if (objects[index] != null) UnityEngine.Object.DestroyImmediate(objects[index]);
            objects.Clear(); committed.Clear(); starts.Clear(); ends.Clear();
        }

        [UnityTest]
        public IEnumerator OriginalBufferDefersTheOnlyConsumptionAndCanUpdateDirectionBeforeCommit()
        {
            player.Dash();
            Assert.That(player.StateMachine.GetState().name, Is.EqualTo("Dashing"));
            Invoke("FixedUpdate");
            Assert.That(committed, Is.Empty);
            Assert.That(runtime.AvailableCharges, Is.EqualTo(2));
            Assert.That(player.CurrentDashUseId, Is.Zero);
            Assert.That(hitbox.excludeLayers.value, Is.EqualTo(2));
            player.SetDirection(Vector2.up);
            float deadline = Time.realtimeSinceStartup + 2f;
            while (starts.Count == 0 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(committed.Count, Is.EqualTo(1));
            Assert.That(committed[0].Direction, Is.EqualTo(Vector2.up));
            Assert.That(committed[0].Duration, Is.EqualTo(0.3f).Within(0.0001f));
            Assert.That(runtime.AvailableCharges, Is.EqualTo(1));
            Assert.That(stats.currentStats.dashCharges, Is.EqualTo(1));
            Assert.That(starts, Is.EqualTo(new[] { 1UL }));
            Assert.That(hitbox.excludeLayers.value, Is.EqualTo(8));
        }

        [TestCase("owner")]
        [TestCase("selection")]
        [TestCase("disable")]
        [TestCase("canonical_death")]
        [TestCase("unbind")]
        public void BufferCancellationConsumesNothingAndNeverReplaysTheOldInput(string reason)
        {
            player.Dash();
            CancelThrough(reason);
            Assert.That(player.CurrentDashUseId, Is.Zero);
            Assert.That(runtime.AvailableCharges, Is.EqualTo(2));
            Assert.That(starts, Is.Empty);
            Assert.That(ends, Is.Empty);
            Assert.That(committed, Is.Empty);
            Assert.That(player.StateMachine.GetState().name, Is.EqualTo("Moving"));
            Assert.That(hitbox.excludeLayers.value, Is.EqualTo(2));
            Assert.That(obstacle.excludeLayers.value, Is.EqualTo(4));
            RestoreAfter(reason);
            ExpireBuffer();
            Invoke("FixedUpdate");
            Assert.That(committed, Is.Empty);
        }

        [TestCase("owner")]
        [TestCase("selection")]
        [TestCase("disable")]
        [TestCase("canonical_death")]
        [TestCase("unbind")]
        public void CommittedDashCancellationEndsOnceRestoresMasksAndNeverRefunds(string reason)
        {
            StartImmediately();
            Assert.That(starts.Count, Is.EqualTo(1));
            CancelThrough(reason);
            player.CancelDash();
            Assert.That(runtime.AvailableCharges, Is.EqualTo(1));
            Assert.That(ends, Is.EqualTo(new[] { 1UL }));
            Assert.That(player.CurrentDashUseId, Is.Zero);
            Assert.That(player.body.linearVelocity, Is.EqualTo(Vector2.zero));
            Assert.That(hitbox.excludeLayers.value, Is.EqualTo(2));
            Assert.That(obstacle.excludeLayers.value, Is.EqualTo(4));
        }

        [Test]
        public void MissingRuntimeDisabledOwnerAndDeadPlayerCannotStartEvenWithALegacyChargeMirror()
        {
            player.UnbindDashRuntime();
            stats.currentStats.dashCharges = 99;
            player.Dash();
            Assert.That(player.StateMachine.GetState().name, Is.EqualTo("Moving"));
            player.BindDashRuntime(runtime, () => now, Commit);
            player.enabled = false;
            player.Dash();
            player.enabled = true;
            player.SetLocalOwnerBound(false);
            player.Dash();
            player.SetLocalOwnerBound(true);
            combatant.ApplyCanonicalHealth(0, 100, 10);
            player.Dash();
            Assert.That(player.StateMachine.GetState().name, Is.EqualTo("Moving"));
            Assert.That(committed, Is.Empty);
            Assert.That(runtime.AvailableCharges, Is.EqualTo(2));
        }

        [Test]
        public void CallbackDenialDoesNotChangeMasksStartEffectsOrConsumeInMovement()
        {
            int requests = 0;
            player.BindDashRuntime(runtime, () => now, motion => { requests++; return 0UL; });
            StartImmediately();
            Assert.That(requests, Is.EqualTo(1));
            Assert.That(runtime.AvailableCharges, Is.EqualTo(2));
            Assert.That(starts, Is.Empty);
            Assert.That(ends, Is.Empty);
            Assert.That(player.StateMachine.GetState().name, Is.EqualTo("Moving"));
            Assert.That(hitbox.excludeLayers.value, Is.EqualTo(2));
        }

        [Test]
        public void SynchronousCommitCancellationKeepsConsumptionButSuppressesMotionAndStart()
        {
            player.BindDashRuntime(runtime, () => now, motion =>
            {
                ulong useId = Commit(motion);
                player.SetLocalOwnerBound(false);
                return useId;
            });
            StartImmediately();
            Assert.That(committed.Count, Is.EqualTo(1));
            Assert.That(runtime.AvailableCharges, Is.EqualTo(1));
            Assert.That(starts, Is.Empty);
            Assert.That(ends, Is.Empty);
            Assert.That(player.CurrentDashUseId, Is.Zero);
            Assert.That(player.body.linearVelocity, Is.EqualTo(Vector2.zero));
            Assert.That(hitbox.excludeLayers.value, Is.EqualTo(2));
        }

        [Test]
        public void SynchronousStartCancellationStopsLaterSubscribersAndFixedUpdateMotion()
        {
            int laterStarts = 0;
            player.OnDashStart += () => player.CancelDash(player.CurrentDashUseId);
            player.OnDashStart += () => laterStarts++;
            StartImmediately();
            Assert.That(starts.Count, Is.EqualTo(1));
            Assert.That(ends, Is.EqualTo(new[] { 1UL }));
            Assert.That(laterStarts, Is.Zero);
            Assert.That(runtime.AvailableCharges, Is.EqualTo(1));
            Assert.That(player.body.linearVelocity, Is.EqualTo(Vector2.zero));
            Assert.That(player.CurrentDashUseId, Is.Zero);
        }

        [Test]
        public void AnExpiredServerRejectionCannotCancelTheNextDashOrItsBuffer()
        {
            StartImmediately();
            ulong first = player.CurrentDashUseId;
            player.CancelDash(first);
            now = runtime.NextUseAt + 0.01d;
            player.Dash();
            player.CancelDash(first);
            Assert.That(player.StateMachine.GetState().name, Is.EqualTo("Dashing"));
            ExpireBuffer();
            Invoke("FixedUpdate");
            ulong second = player.CurrentDashUseId;
            Assert.That(second, Is.Not.Zero.And.Not.EqualTo(first));
            player.CancelDash(first);
            Assert.That(player.CurrentDashUseId, Is.EqualTo(second));
            player.CancelDash(second);
            Assert.That(ends, Is.EqualTo(new[] { first, second }));
        }

        [Test]
        public void EveryDashRecomputesDurationAndThenKeepsItsPeakSpeedFrozen()
        {
            StartImmediately();
            float originalDuration = player.TotalDashTime;
            player.CancelDash();
            now = runtime.NextUseAt + 0.01d;
            stats.currentStats.dashSpeed = 40f;
            StartImmediately();
            Assert.That(player.DashDistance, Is.EqualTo(6f));
            Assert.That(player.TotalDashTime, Is.EqualTo(originalDuration / 2f).Within(0.0001f));
            Assert.That(committed[1].PeakSpeed, Is.EqualTo(40f));
            stats.currentStats.dashSpeed = 400f;
            Invoke("FixedUpdate");
            Assert.That(player.body.linearVelocity.magnitude, Is.EqualTo(40f).Within(0.0001f));
            Assert.That(player.TotalDashTime, Is.EqualTo(originalDuration / 2f).Within(0.0001f));
        }

        [Test]
        public void CurveIntegralUsesTheSourceTrapezoidRuleAndZeroDistanceEndsWithoutNan()
        {
            SetField("dashCurve", AnimationCurve.Linear(0f, 0f, 1f, 1f));
            StartImmediately();
            Assert.That(player.TotalDashTime, Is.EqualTo(0.6f).Within(0.0001f));
            player.CancelDash();
            now = runtime.NextUseAt + 0.01d;
            stats.currentStats.dashDistance = 0f;
            StartImmediately();
            Assert.That(committed[1].Distance, Is.Zero);
            Assert.That(committed[1].Duration, Is.Zero);
            Assert.That(starts.Count, Is.EqualTo(2));
            Assert.That(ends.Count, Is.EqualTo(2));
            Assert.That(player.CurrentDashUseId, Is.Zero);
            Assert.That(float.IsNaN(player.body.linearVelocity.x), Is.False);
            Assert.That(float.IsNaN(player.body.linearVelocity.y), Is.False);
        }

        [Test]
        public void InvalidCurveAndSpeedFailBeforeThePermissionCallback()
        {
            SetField("dashCurve", new AnimationCurve());
            StartImmediately();
            SetField("dashCurve", AnimationCurve.Linear(0f, 1f, 1f, 1f));
            stats.currentStats.dashSpeed = 0f;
            StartImmediately();
            Assert.That(committed, Is.Empty);
            Assert.That(starts, Is.Empty);
            Assert.That(runtime.AvailableCharges, Is.EqualTo(2));
            Assert.That(player.StateMachine.GetState().name, Is.EqualTo("Moving"));
        }

        [Test]
        public void EdgeCollisionResolvesMotionBeforePermissionAndUsesTheSameDashMasks()
        {
            GameObject edge = Create("Dash Test Edge");
            edge.layer = 9;
            edge.transform.position = Vector3.right * 3f;
            edge.AddComponent<BoxCollider2D>();
            player.obstacleLayerMask = 1 << 9;
            player.edgeLayerMask = 1 << 9;
            Physics2D.SyncTransforms();
            StartImmediately();
            Assert.That(committed.Count, Is.EqualTo(1));
            Assert.That(committed[0].StartPosition, Is.EqualTo(Vector2.zero));
            Assert.That(committed[0].Distance, Is.EqualTo(2.5f).Within(0.0001f));
            Assert.That(committed[0].Duration, Is.EqualTo(0.125f).Within(0.0001f));
            Assert.That(hitbox.excludeLayers.value, Is.EqualTo(8));
            Assert.That(obstacle.excludeLayers.value, Is.EqualTo(8));
        }

        [Test]
        public void ServerCanResolveIdenticalMotionWhileDisabledWithoutOwnerRuntimeOrSideEffects()
        {
            player.UnbindDashRuntime();
            player.SetLocalOwnerBound(false);
            player.enabled = false;
            Assert.That(player.TryGetDashMotionParameters(Vector2.up * 4f, new Vector2(10f, 15f), out DashMotionParameters motion), Is.True);
            Assert.That(motion.StartPosition, Is.EqualTo(new Vector2(10f, 15f)));
            Assert.That(motion.Direction, Is.EqualTo(Vector2.up));
            Assert.That(motion.Distance, Is.EqualTo(6f));
            Assert.That(motion.PeakSpeed, Is.EqualTo(20f));
            Assert.That(motion.Duration, Is.EqualTo(0.3f).Within(0.0001f));
            Assert.That(player.CurrentDashUseId, Is.Zero);
            Assert.That(player.StateMachine.GetState().name, Is.EqualTo("Moving"));
            Assert.That(committed, Is.Empty);
            Assert.That(runtime.AvailableCharges, Is.EqualTo(2));
            Assert.That(hitbox.excludeLayers.value, Is.EqualTo(2));
            Assert.That(player.TryGetDashMotionParameters(Vector2.zero, Vector2.zero, out _), Is.False);
        }

        [Test]
        public void NormalFixedTickCompletionEndsOnceAndCannotBypassTheRuntimeChainGate()
        {
            StartImmediately();
            for (int tick = 0; tick < 30 && player.CurrentDashUseId != 0; tick++) Invoke("FixedUpdate");
            Assert.That(player.CurrentDashUseId, Is.Zero);
            Assert.That(player.StateMachine.GetState().name, Is.EqualTo("Moving"));
            Assert.That(ends, Is.EqualTo(new[] { 1UL }));
            player.Dash();
            Assert.That(player.StateMachine.GetState().name, Is.EqualTo("Moving"), "The injected clock has not reached NextUseAt.");
            Assert.That(committed.Count, Is.EqualTo(1));
            Assert.That(runtime.AvailableCharges, Is.EqualTo(1));
            Assert.That(hitbox.excludeLayers.value, Is.EqualTo(2));
        }

        [Test]
        public void UpdateRefreshesChargeMirrorAndCapacityChangesKeepTheSpentChargePending()
        {
            StartImmediately();
            player.CancelDash();
            stats.currentStats.dashCharges = 99;
            Invoke("Update");
            Assert.That(stats.currentStats.dashCharges, Is.EqualTo(1));
            int capacityChanges = 0;
            stats.MaximumDashesChanged += _ => capacityChanges++;
            stats.StatMultipliers.extraDashCharges = 1;
            stats.UpdateMaxDashes();
            stats.UpdateMaxDashes();
            Assert.That(capacityChanges, Is.EqualTo(1));
            Assert.That(runtime.MaxCharges, Is.EqualTo(3));
            Assert.That(runtime.AvailableCharges, Is.EqualTo(2));
            Assert.That(stats.currentStats.dashCharges, Is.EqualTo(2));
            stats.EvaluateModifiers();
            Assert.That(capacityChanges, Is.EqualTo(2), "The normal stats evaluation emits a maximum change too.");
            Assert.That(runtime.MaxCharges, Is.EqualTo(2));
            Assert.That(runtime.AvailableCharges, Is.EqualTo(1));
            now += 2.5d;
            Invoke("Update");
            Assert.That(stats.currentStats.dashCharges, Is.EqualTo(2));
        }

        [Test]
        public void MaximumStatsNotificationAloneDoesNotWriteASecondCurrentChargeTruth()
        {
            var detached = new PlayerStats();
            detached.Initialize(stats.playerBaseStatsDatabase.values);
            detached.currentStats.dashCharges = 1;
            detached.StatMultipliers.extraDashCharges = 2;
            detached.UpdateMaxDashes();
            Assert.That(detached.currentStats.maxDashCharges, Is.EqualTo(4));
            Assert.That(detached.currentStats.dashCharges, Is.EqualTo(1));
        }

        [Test]
        public void DestroyUnsubscribesCapacityAndCanonicalHealthAndDoesNotRestoreSpentCharges()
        {
            StartImmediately();
            UnityEngine.Object.DestroyImmediate(player);
            Assert.That(ends, Is.EqualTo(new[] { 1UL }));
            Assert.That(runtime.AvailableCharges, Is.EqualTo(1));
            stats.StatMultipliers.extraDashCharges = 5;
            Assert.DoesNotThrow(stats.UpdateMaxDashes);
            Assert.That(runtime.MaxCharges, Is.EqualTo(2));
            Assert.DoesNotThrow(() => combatant.ApplyCanonicalHealth(0, 100, 10));
        }

        [Test]
        public void BindingAgainDoesNotDuplicateCapacitySubscriptionsOrCancelTheCurrentDash()
        {
            StartImmediately();
            ulong current = player.CurrentDashUseId;
            player.BindDashRuntime(runtime, () => now, Commit);
            Assert.That(player.CurrentDashUseId, Is.EqualTo(current));
            Assert.That(ends, Is.Empty);
            player.UnbindDashRuntime();
            stats.StatMultipliers.extraDashCharges = 1;
            stats.UpdateMaxDashes();
            Assert.That(runtime.MaxCharges, Is.EqualTo(2));
            Assert.That(ends, Is.EqualTo(new[] { current }));
        }

        private ulong Commit(DashMotionParameters motion)
        {
            if (!runtime.TryConsume(now, motion.Duration, stats.currentStats.dashCooldown)) return 0;
            committed.Add(motion);
            return ++nextUseId;
        }

        private void StartImmediately()
        {
            player.Dash();
            ExpireBuffer();
            Invoke("FixedUpdate");
        }

        private void ExpireBuffer()
        {
            FieldInfo field = typeof(PlayerMovement).GetField("_dashBuffer", BindingFlags.NonPublic | BindingFlags.Instance);
            var buffer = (ActionBuffer)field.GetValue(player);
            buffer.Consume();
            field.SetValue(player, buffer);
        }

        private void CancelThrough(string reason)
        {
            switch (reason)
            {
                case "owner": player.SetLocalOwnerBound(false); break;
                case "selection": player.SetUpgradeSelectionLocked(true); break;
                case "disable": player.enabled = false; break;
                case "canonical_death": combatant.ApplyCanonicalHealth(0, 100, 10); break;
                case "unbind": player.UnbindDashRuntime(); break;
                default: throw new ArgumentOutOfRangeException(nameof(reason));
            }
        }

        private void RestoreAfter(string reason)
        {
            switch (reason)
            {
                case "owner": player.SetLocalOwnerBound(true); break;
                case "selection": player.SetUpgradeSelectionLocked(false); break;
                case "disable": player.enabled = true; break;
                case "canonical_death": combatant.ApplyCanonicalHealth(100, 100, 11); break;
                case "unbind": player.BindDashRuntime(runtime, () => now, Commit); break;
            }
        }

        private object Invoke(string method) => typeof(PlayerMovement)
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player, null);

        private void SetField(string field, object value) => typeof(PlayerMovement)
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(player, value);

        private GameObject Create(string name, bool active = true)
        {
            var go = new GameObject(name); go.SetActive(active); objects.Add(go); return go;
        }
    }

    public sealed class DashMovementTestAnimator : PlayerAnimator
    {
        protected override void OnEnable() { }
        public new void OnDisable() { }
        public override void Movement(float value, float x, float y) { }
        public override void Dash(float x, float y) { }
        public override void Hurt(float x, float y) { }
        public override void Dead(float x, float y) { }
    }
}
