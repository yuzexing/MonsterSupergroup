using System;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class WaveScheduleTests
    {
        private static WaveParameters Defaults() => new WaveParameters(30, 6, 2, 30);
        [Test]
        public void ThreeWaves_EmitExactlyEighteenDistinctSlots_WithoutClearingPreviousEnemies()
        {
            var schedule = new ServerWaveSchedule("run", Defaults(), 100);
            long sequence = 0;
            for (int second = 0; second <= 70; second++)
                if (schedule.Tick(100 + second, true, (int)sequence, out var slot))
                {
                    Assert.That(slot.Sequence, Is.EqualTo(++sequence));
                    Assert.That(second % 30, Is.EqualTo((slot.Index - 1) * 2));
                    Assert.That(schedule.Resolve(slot, true), Is.True);
                    Assert.That(schedule.Resolve(slot, true), Is.False, "One decision cannot be applied twice.");
                }
            Assert.That(sequence, Is.EqualTo(18));
            Assert.That(schedule.State.Wave, Is.EqualTo(3));
            Assert.That(schedule.State.Spawned, Is.EqualTo(6));
            Assert.That(schedule.State.TotalSkipped, Is.Zero);
        }
        [Test]
        public void Pause_ResumesRemainingTime_WithoutOfflineCatchup()
        {
            var schedule = new ServerWaveSchedule("run", Defaults(), 0);
            schedule.Tick(0, true, 0, out var first); schedule.Resolve(first, true);
            schedule.Tick(1, true, 1, out _);
            Assert.That(schedule.Tick(1.5, false, 1, out _), Is.False);
            Assert.That(schedule.Tick(100, false, 1, out _), Is.False);
            Assert.That(schedule.State.Phase, Is.EqualTo(WavePhase.Paused));
            Assert.That(schedule.State.Elapsed, Is.EqualTo(1));
            Assert.That(schedule.Tick(200, true, 1, out _), Is.False);
            Assert.That(schedule.Tick(201, true, 1, out var second), Is.True);
            Assert.That(second.Sequence, Is.EqualTo(2));
        }
        [Test]
        public void FullCapacityDecisions_AreConsumed_NotRetriedWhenSpaceReturns()
        {
            var schedule = new ServerWaveSchedule("run", Defaults(), 0);
            schedule.Tick(0, true, 30, out var first); schedule.Resolve(first, false);
            Assert.That(schedule.Tick(1, true, 29, out _), Is.False);
            schedule.Tick(2, true, 29, out var second); schedule.Resolve(second, true);
            Assert.That(schedule.State.TotalSpawned, Is.EqualTo(1));
            Assert.That(schedule.State.TotalSkipped, Is.EqualTo(1));
        }
        [Test]
        public void ClockJump_ConsumesOnlyLatestSlot_AndCountsSkippedSlotsAcrossWaves()
        {
            var schedule = new ServerWaveSchedule("run", Defaults(), 0);
            Assert.That(schedule.Tick(65, true, 0, out var slot), Is.True);
            Assert.That(slot.Sequence, Is.EqualTo(15));
            Assert.That(slot.Wave, Is.EqualTo(3));
            Assert.That(slot.Index, Is.EqualTo(3));
            schedule.Resolve(slot, true);
            Assert.That(schedule.State.TotalSkipped, Is.EqualTo(14));
            Assert.That(schedule.State.Skipped, Is.EqualTo(2));
            Assert.That(schedule.Tick(65, true, 1, out _), Is.False);
        }
        [Test]
        public void Stop_DropsOutstandingDecision_AndNewRunStartsFresh()
        {
            var schedule = new ServerWaveSchedule("old", Defaults(), 0);
            schedule.Tick(0, true, 0, out var slot); schedule.Stop();
            Assert.That(schedule.Resolve(slot, true), Is.False);
            Assert.That(schedule.Tick(500, true, 0, out _), Is.False);
            var next = new ServerWaveSchedule("new", Defaults(), 500);
            Assert.That(next.Tick(500, true, 0, out slot), Is.True);
            Assert.That(slot.Sequence, Is.EqualTo(1));
            Assert.That(next.State.RunId, Is.EqualTo("new"));
        }
        [Test]
        public void Rules_AreCaptured_NotLiveReadDuringRun()
        {
            var rules = ScriptableObject.CreateInstance<GameplayWaveRules>();
            try
            {
                Assert.That(rules.TryCapture(out var captured, out _), Is.True);
                var serialized = new SerializedObject(rules);
                serialized.FindProperty("enemiesPerWave").intValue = 2;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(rules.TryCapture(out var changed, out _), Is.True);
                Assert.That(captured.Count, Is.EqualTo(6));
                Assert.That(changed.Count, Is.EqualTo(2));
                Assert.Throws<ArgumentException>(() => new WaveParameters(10, 6, 2, 30));
                Assert.Throws<ArgumentException>(() => new WaveParameters(double.NaN, 6, 2, 30));
                Assert.Throws<ArgumentException>(() => new WaveParameters(30, 6, 2, 0));
            }
            finally { UnityEngine.Object.DestroyImmediate(rules); }
        }
        [Test]
        public void Snapshot_RoundTripsFullReconnectState_ThroughMirror()
        {
            var snapshot = new WaveProgressSnapshot { RunId = "run-42", Phase = WavePhase.Paused,
                Wave = 3, Elapsed = 64.5, WaveDuration = 30, Planned = 6, Spawned = 2, Skipped = 1,
                Alive = 12, Limit = 30, TotalSpawned = 14, TotalSkipped = 1 };
            var writer = new NetworkWriter(); writer.Write(snapshot);
            var reader = new NetworkReader(writer.ToArraySegment());
            var restored = reader.Read<WaveProgressSnapshot>();
            Assert.That(restored, Is.EqualTo(snapshot));
            Assert.That(restored.Remaining, Is.EqualTo(25.5));
        }
        [Test]
        public void ServerOnlyLaunch_UsesExistingKcpOptions()
        {
            Assert.That(KcpLocalLaunchOptions.TryParse(new[] { "--kcp-role=server", "--kcp-port=7960" }, out var options, out _), Is.True);
            Assert.That(options.Role, Is.EqualTo(KcpLocalRole.Server));
            Assert.That(options.Port, Is.EqualTo(7960));
        }
    }
}
