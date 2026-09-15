using System;
using System.Linq;
using MonsterSupergroup.NetworkCombat.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class LimboReferenceTests
    {
        private static ReferenceSpawnDefinition Clip(ReferenceSpawnMode mode, int count = 3, double end = 10) =>
            new ReferenceSpawnDefinition { Mode = mode, SourceEnemy = "fixture", Start = 0, End = end, Count = count,
                Timestamps = mode == ReferenceSpawnMode.CurveBudget ? ReferenceWaveProgram.SampleBudget(AnimationCurve.Linear(0, 1, 1, 1), count, end) : Array.Empty<float>() };

        private static ServerWaveSchedule Schedule(ReferenceSpawnDefinition clip, double end = 30, int limit = 1000)
        {
            var program = new ReferenceWaveProgram(new[] { clip }, Array.Empty<ReferenceBarrierDefinition>(), end, 841.5766649882,
                AnimationCurve.Linear(0, 0, 1, 1), 1.5f, 1, 2, 1.5f, 30, 5, 20, 6, 1.41f, 1, 1);
            return new ServerWaveSchedule("reference-test", new WaveParameters(program, Array.Empty<GameObject>(), limit, 100), 0);
        }

        [Test]
        public void ExportedLimboKeepsAllThirtyOneClipsAndDoesNotSubstituteRusherVariant()
        {
            var rules = AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot + "/Full.asset");
            Assert.That(rules, Is.Not.Null, "Create the reference assets before validation.");
            Assert.That(rules.TryCapture(out var parameters, out var error), Is.True, error);
            var reference = parameters.Reference;
            Assert.That(reference.Clips.Length, Is.EqualTo(31));
            Assert.That(reference.Clips.Where(c => c.Mode == ReferenceSpawnMode.CurveBudget).Sum(c => c.Count), Is.EqualTo(1139));
            Assert.That(reference.Clips.Where(c => c.Mode == ReferenceSpawnMode.FormationBurst).Sum(c => c.Count), Is.EqualTo(60));
            Assert.That(reference.Clips.Count(c => c.Mode == ReferenceSpawnMode.AliveTarget), Is.EqualTo(10));
            var rusher = reference.Clips.Single(c => Math.Abs(c.Start - 509.65) < .001);
            Assert.That(rusher.Variant, Is.EqualTo(1)); Assert.That(rusher.Stats.BaseHealth, Is.EqualTo(150));
            Assert.That(rusher.Stats.BaseSpeed, Is.EqualTo(4));
            Assert.That(reference.SourceDuration, Is.EqualTo(841.5766649882).Within(.0001));
            Assert.Throws<ArgumentException>(() => new ServerWaveSchedule("blocked", parameters, 0), "Missing attack data cannot silently become enabled.");
        }

        [Test]
        public void OpeningRetainsFull135SecondBudgetAndOriginalXpDenominator()
        {
            var rules = AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot + "/Opening.asset");
            Assert.That(rules.TryCapture(out var parameters, out var error), Is.True, error);
            var reference = parameters.Reference; var clip = reference.Clips[0];
            Assert.That(reference.EndTime, Is.EqualTo(60)); Assert.That(clip.Start, Is.EqualTo(1)); Assert.That(clip.End, Is.EqualTo(136));
            Assert.That(clip.Count, Is.EqualTo(250)); Assert.That(clip.Stats.BaseHealth, Is.EqualTo(20));
            Assert.That(clip.Stats.BaseDamage, Is.EqualTo(50)); Assert.That(clip.Stats.KnockBackMultiplier, Is.EqualTo(1));
            Assert.That(clip.Timestamps[0], Is.EqualTo(0));
            Assert.That(clip.Timestamps[1], Is.EqualTo(.9502606f).Within(.00005f));
            Assert.That(clip.Timestamps[2], Is.EqualTo(1.8627874f).Within(.00005f));
            Assert.That(clip.Timestamps.Count(t => t + 1 < 60), Is.EqualTo(104), "Ideal opportunities, not actual runtime births.");
            Assert.That(reference.XpMultiplier(60), Is.LessThan(1.5));
        }

        [Test]
        public void CurveStartsStrictlyAfterBoundaryDoesNotCatchUpAndConsumesPositionFailure()
        {
            var clock = Schedule(Clip(ReferenceSpawnMode.CurveBudget, 3, 12)); var alive = new int[1];
            Assert.That(clock.TickReference(0, 0, true, 0, 0, alive, out _), Is.False);
            Assert.That(clock.TickReference(.01, 1, true, 0, 0, alive, out _), Is.False);
            Assert.That(clock.TickReference(.02, 2, true, 0, 0, alive, out var first), Is.True);
            Assert.That(clock.Resolve(first, false), Is.True);
            Assert.That(clock.TickReference(.02, 2, true, 0, 0, alive, out _), Is.False);
            Assert.That(clock.TickReference(10, 3, true, 0, 0, alive, out _), Is.False);
            Assert.That(clock.TickReference(10.01, 4, true, 0, 0, alive, out var second), Is.True);
            clock.Resolve(second, true);
            Assert.That(clock.TickReference(10.01, 4, true, 1, 1, alive, out _), Is.False);
            Assert.That(clock.TickReference(10.02, 5, true, 1, 1, alive, out _), Is.False);
            Assert.That(clock.State.TotalAttempts, Is.EqualTo(2)); Assert.That(clock.State.TotalSkipped, Is.EqualTo(1));
        }

        [Test]
        public void CurveAbandonsRemainingBudgetAtExactCapAndDoesNotResumeAfterKill()
        {
            var clock = Schedule(Clip(ReferenceSpawnMode.CurveBudget, 4), limit: 2); var alive = new int[1];
            clock.TickReference(.01, 1, true, 1, 1, alive, out _);
            Assert.That(clock.TickReference(.02, 2, true, 1, 1, alive, out var spawn), Is.True);
            clock.Resolve(spawn, true);
            Assert.That(clock.State.AbandonedBudget, Is.GreaterThanOrEqualTo(3));
            Assert.That(clock.TickReference(5, 3, true, 0, 0, alive, out _), Is.False);
        }

        [Test]
        public void LimitedBatchCanOverrunEndAndExactGlobalCapThenStopsWithoutWaitingForDeaths()
        {
            var clock = Schedule(Clip(ReferenceSpawnMode.AliveTarget, 3, .025), limit: 2); var alive = new int[1];
            clock.TickReference(.01, 1, true, 1, 1, alive, out _);
            for (int i = 0; i < 3; i++)
            {
                Assert.That(clock.TickReference(.02 + .02 * i, 2 + 2 * i, true, 1 + i, 1 + i, alive, out var spawn), Is.True);
                clock.Resolve(spawn, true); alive[0]++;
                Assert.That(clock.TickReference(.03 + .02 * i, 3 + 2 * i, true, 2 + i, 2 + i, alive, out _), Is.False);
            }
            Assert.That(clock.TickReference(.1, 8, true, 4, 4, alive, out _), Is.False);
            Assert.That(clock.State.TotalSpawned, Is.EqualTo(3)); Assert.That(clock.State.ActiveClips, Is.Zero);
        }

        [Test]
        public void LimitedSpawnerReplenishesKillsButDoesNotHaveAFiniteTotalBudget()
        {
            var clock = Schedule(Clip(ReferenceSpawnMode.AliveTarget, 1)); var alive = new int[1];
            int births = 0;
            for (int frame = 1; frame <= 25; frame++)
                if (clock.TickReference(frame * .1, frame, true, 0, 0, alive, out var spawn))
                { clock.Resolve(spawn, true); births++; /* confirmed kill before next tick */ }
            Assert.That(births, Is.GreaterThan(5)); Assert.That(clock.State.Planned, Is.Zero);
        }

        [Test]
        public void BurstCapturesAfterOneSecondSpawnsTogetherAfterTwoAndBypassesGlobalCount()
        {
            var clock = Schedule(Clip(ReferenceSpawnMode.FormationBurst, 5)); var alive = new int[1]; int captured = 0;
            clock.FormationCaptured += _ => captured++;
            Assert.That(clock.TickReference(.01, 1, true, 1000, 1000, alive, out _), Is.False);
            Assert.That(clock.TickReference(1.1, 2, true, 1000, 1000, alive, out _), Is.False);
            Assert.That(captured, Is.EqualTo(1));
            for (int i = 0; i < 5; i++)
            {
                Assert.That(clock.TickReference(2.1, 3, true, 1000 + i, 1000, alive, out var spawn), Is.True);
                Assert.That(spawn.FormationIndex, Is.EqualTo(i)); clock.Resolve(spawn, true);
                Assert.That(clock.State.CountedAlive, Is.EqualTo(1000));
            }
            Assert.That(clock.TickReference(2.1, 3, true, 1005, 1000, alive, out _), Is.False);
            Assert.That(clock.State.TotalSpawned, Is.EqualTo(5));
        }

        [Test]
        public void OccupiedTrapSkipsBurstWithoutReschedulingAndPauseDoesNotConsumeStageTime()
        {
            var clock = Schedule(Clip(ReferenceSpawnMode.FormationBurst, 5), end: 6); var alive = new int[1];
            Assert.That(clock.TryAcquireBarrierSlot(), Is.True);
            clock.TickReference(.01, 1, true, 0, 0, alive, out _);
            Assert.That(clock.State.TotalSkipped, Is.EqualTo(5)); clock.ReleaseBarrierSlot();
            clock.TickReference(2, 2, false, 0, 0, alive, out _);
            clock.TickReference(12, 3, false, 0, 0, alive, out _);
            clock.TickReference(13, 4, true, 0, 0, alive, out _);
            Assert.That(clock.State.Elapsed, Is.EqualTo(.01).Within(.00001));
            Assert.That(clock.TickReference(20, 5, true, 0, 0, alive, out _), Is.False);
            Assert.That(clock.State.Phase, Is.EqualTo(WavePhase.Completed)); Assert.That(clock.State.TotalSpawned, Is.Zero);
        }

        [Test]
        public void HitchBeforeFormationCaptureDoesNotConsumeTheSecondDelay()
        {
            var clock = Schedule(Clip(ReferenceSpawnMode.FormationBurst, 2)); var alive = new int[1];
            clock.TickReference(.01, 1, true, 0, 0, alive, out _);
            Assert.That(clock.TickReference(5, 2, true, 0, 0, alive, out _), Is.False);
            Assert.That(clock.TickReference(5.9, 3, true, 0, 0, alive, out _), Is.False);
            Assert.That(clock.TickReference(6, 4, true, 0, 0, alive, out _), Is.True);
        }

        [Test]
        public void ReferenceConditionResetHealsThroughCanonicalLedgerWithoutAKill()
        {
            var gateway = new ServerCombatGateway();
            gateway.Ledger.RegisterEntity(7, 20, CombatEntityKind.Enemy, CombatEntityAuthority.ServerCanonical);
            gateway.Ledger.ApplyServerStatusDamage(7, 15, 1, 2);
            Assert.That(gateway.Ledger.TryGetState(7, out var before), Is.True);
            Assert.That(before.Health, Is.EqualTo(5));
            var batch = gateway.ResetEnemyCondition(7);
            Assert.That(gateway.Ledger.TryGetState(7, out var after), Is.True);
            Assert.That(after.Health, Is.EqualTo(20)); Assert.That(after.MaxHealth, Is.EqualTo(20));
            Assert.That(after.StateVersion, Is.GreaterThan(before.StateVersion));
            Assert.That(batch.ConfirmedKills, Is.Empty);
        }

        [Test]
        public void RepositionEpochRejectsOldOwnerMovementWithoutChangingOwner()
        {
            var registry = new ServerEnemySimulationRegistry();
            registry.RegisterEnemy(7, Vector2.zero, 0);
            var initial = registry.AssignClientOwner(7, 2, 2);
            var renewed = registry.RenewAssignment(7);
            Assert.That(renewed.SimulationOwnerPlayerId, Is.EqualTo(initial.SimulationOwnerPlayerId));
            Assert.That(renewed.Epoch, Is.GreaterThan(initial.Epoch));
            var packet = new EnemySimulationSnapshot { EnemyEntityId = 7, AssignmentEpoch = initial.Epoch, Sequence = 1, SampleNetworkTime = 1 };
            Assert.That(registry.TryAcceptClientSnapshot(2, packet), Is.EqualTo(EnemySnapshotRejectionReason.WrongEpoch));
        }
    }
}
