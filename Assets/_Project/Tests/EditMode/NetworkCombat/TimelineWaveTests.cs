using System;
using System.Collections.Generic;
using System.Linq;
using MonsterSupergroup.NetworkCombat.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class TimelineWaveTests
    {
        private readonly List<Object> owned = new List<Object>();
        private TimelineAsset Make(double duration = 30)
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>(); owned.Add(timeline);
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength; timeline.fixedDuration = duration;
            return timeline;
        }
        private NetworkEnemySpawnTrack Add(TimelineAsset timeline, string name, double start, double duration, int count, TrackAsset parent = null)
        {
            var prefab = new GameObject(name); owned.Add(prefab);
            var track = timeline.CreateTrack<NetworkEnemySpawnTrack>(parent, name); owned.Add(track);
            var clip = track.CreateClip<NetworkEnemySpawnClip>(); owned.Add(clip.asset);
            clip.start = start; clip.duration = duration;
            ((NetworkEnemySpawnClip)clip.asset).enemyPrefab = prefab; ((NetworkEnemySpawnClip)clip.asset).count = count;
            return track;
        }
        [TearDown] public void Cleanup() { for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]); owned.Clear(); }

        [Test]
        public void DefaultTimelineCompilesSixWavesWithExactTypesTimesAndUniqueSlots()
        {
            var rules = AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(NetworkWaveTimelineEditorUtility.RulesPath);
            Assert.That(rules.TryCapture(out var settings, out var error), Is.True, error);
            var expected = new[] { "NetworkEnemyBase", "NetworkEnemyBase", "NetworkEnemySkeleton", "NetworkEnemyImp", "NetworkEnemyLustSinner", "NetworkEnemyLustSinner" };
            var schedule = new ServerWaveSchedule("six", settings, 0); var emitted = new List<WaveSpawnOpportunity>();
            for (double time = 0; time <= 160; time += .25)
                while (schedule.Tick(time, true, 0, out var spawn)) { emitted.Add(spawn); schedule.Resolve(spawn, true); }
            Assert.That(emitted.Count, Is.EqualTo(36)); Assert.That(schedule.State.TotalSkipped, Is.Zero);
            for (int i = 0; i < emitted.Count; i++)
            {
                var spawn = emitted[i]; int wave = i / 6, slot = i % 6;
                Assert.That(spawn.Sequence, Is.EqualTo(i + 1)); Assert.That(spawn.Wave, Is.EqualTo(wave + 1));
                Assert.That(spawn.Index, Is.EqualTo(slot + 1)); Assert.That(spawn.ScheduledTime, Is.EqualTo(wave * 30 + slot * 2).Within(1e-6));
                string prefab = wave >= 1 && wave <= 3 && slot % 2 == 1 ? "NetworkEnemyLustSinner" : expected[wave];
                Assert.That(settings.Prefabs[spawn.PrefabIndex].name, Is.EqualTo(prefab));
            }
        }
        [Test]
        public void SimultaneousSpawnsKeepStableOrderAndSlowFramesSkipOnlyEarlierGroups()
        {
            var timeline = Make(10); Add(timeline, "first", 0, 4, 2); Add(timeline, "second", 2, 2, 1);
            var program = NetworkWaveTimelineCompiler.Compile(timeline, 10, out var prefabs);
            var schedule = new ServerWaveSchedule("group", new WaveParameters(program, prefabs, 30), 0);
            Assert.That(schedule.Tick(2.1, true, 0, out var a), Is.True); Assert.That(a.Sequence, Is.EqualTo(2));
            Assert.That(prefabs[a.PrefabIndex].name, Is.EqualTo("first")); schedule.Resolve(a, true);
            Assert.That(schedule.Tick(2.1, true, 1, out var b), Is.True); Assert.That(b.Sequence, Is.EqualTo(3));
            Assert.That(prefabs[b.PrefabIndex].name, Is.EqualTo("second")); schedule.Resolve(b, false);
            Assert.That(schedule.Tick(2.1, true, 1, out _), Is.False);
            Assert.That(schedule.State.TotalSkipped, Is.EqualTo(2)); Assert.That(schedule.State.TotalSpawned, Is.EqualTo(1));
            Assert.That(schedule.Tick(3, true, 0, out _), Is.False, "Capacity release must not replay a rejected slot.");
        }
        [Test]
        public void LastWaveRepeatsWithoutReplayingPrefixOrDoubleFiringAtBoundary()
        {
            var program = new WaveSpawnProgram(10, 20, new[] { new WaveSpawnEntry(0, 0), new WaveSpawnEntry(10, 1), new WaveSpawnEntry(12, 1) });
            Assert.That(program.LastDue(19.999), Is.EqualTo(3)); Assert.That(program.LastDue(20), Is.EqualTo(4));
            Assert.That(program.Get(4).ScheduledTime, Is.EqualTo(20)); Assert.That(program.Get(4).PrefabIndex, Is.EqualTo(1));
            Assert.That(program.Get(8).Wave, Is.EqualTo(5)); Assert.That(program.CountInWave(100), Is.EqualTo(2));
        }
        [Test]
        public void MutedTracksAndGroupsAreExcludedAndCapturedEventsDoNotChange()
        {
            var timeline = Make(); var group = timeline.CreateTrack<GroupTrack>(); owned.Add(group);
            var track = Add(timeline, "enemy", 0, 12, 6, group);
            var captured = NetworkWaveTimelineCompiler.Compile(timeline, 30, out _);
            ((NetworkEnemySpawnClip)track.GetClips().Single().asset).count = 1;
            Assert.That(captured.EventCount, Is.EqualTo(6));
            group.muted = true; Assert.That(NetworkWaveTimelineCompiler.Compile(timeline, 30, out _).EventCount, Is.Zero);
            group.muted = false; track.muted = true; Assert.That(NetworkWaveTimelineCompiler.Compile(timeline, 30, out _).EventCount, Is.Zero);
        }
        [Test]
        public void EmptyLeadInDoesNotReviveAnEarlierWaveAndLongPauseDoesNotCatchUp()
        {
            var program = new WaveSpawnProgram(10, 20, new[] { new WaveSpawnEntry(0, 0), new WaveSpawnEntry(15, 0) });
            var schedule = new ServerWaveSchedule("gap", new WaveParameters(program, Array.Empty<GameObject>(), 30), 0);
            Assert.That(schedule.Tick(12, true, 0, out _), Is.False);
            Assert.That(schedule.State.TotalSkipped, Is.EqualTo(1));
            Assert.That(schedule.Tick(100, false, 0, out _), Is.False);
            Assert.That(schedule.Tick(200, true, 0, out _), Is.False);
            Assert.That(schedule.Tick(203, true, 0, out var spawn), Is.True); Assert.That(spawn.Sequence, Is.EqualTo(2));
        }
        [Test]
        public void InvalidTimelineConfigurationIsRejectedBeforeRun()
        {
            var timeline = Make(); var track = Add(timeline, "enemy", 0, 12, 6); var clip = track.GetClips().Single(); var spawn = (NetworkEnemySpawnClip)clip.asset;
            timeline.fixedDuration = 31; Assert.Throws<ArgumentException>(() => NetworkWaveTimelineCompiler.Compile(timeline, 30, out _)); timeline.fixedDuration = 30;
            spawn.count = 0; Assert.Throws<ArgumentException>(() => NetworkWaveTimelineCompiler.Compile(timeline, 30, out _)); spawn.count = 6;
            spawn.enemyPrefab = null; Assert.Throws<ArgumentException>(() => NetworkWaveTimelineCompiler.Compile(timeline, 30, out _));
            Assert.Throws<ArgumentException>(() => NetworkWaveTimelineCompiler.Compile(null, 30, out _));
            Assert.Throws<ArgumentException>(() => new WaveSpawnProgram(double.NaN, 30, Array.Empty<WaveSpawnEntry>()));
        }
        [Test] public void DefaultSetupIsIdempotentAndPreservesVariantAssets() => NetworkWaveTimelineEditorUtility.VerifyRepeat();
    }
}
