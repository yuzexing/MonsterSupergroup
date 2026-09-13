using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AstralShift.HellMaiden.AI.Enemy;
using Mirror;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed partial class GameplayWavePlayModeTests
    {
        private void AddFixtureSpawn(TimelineAsset timeline, GameObject prefab, double start, double duration, int count)
        {
            var track = timeline.CreateTrack<NetworkEnemySpawnTrack>(); timelineAssets.Add(track);
            var clip = track.CreateClip<NetworkEnemySpawnClip>(); timelineAssets.Add(clip.asset);
            clip.start = start; clip.duration = duration;
            ((NetworkEnemySpawnClip)clip.asset).enemyPrefab = prefab; ((NetworkEnemySpawnClip)clip.asset).count = count;
        }

        [UnityTest]
        public IEnumerator TimelineSpawnsFourRealVariantsAndRepeatsLastWaveAfterCapacityReleased()
        {
            yield return StartHost();
            var authored = (GameplayWaveRules)typeof(NetworkGameplayEnemySpawner).GetField("waveRules", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(Spawner);
            Assert.That(authored.TryCapture(out var captured, out _), Is.True);
            rulesCopy = Object.Instantiate(authored);
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>(); timelineAssets.Add(timeline);
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength; timeline.fixedDuration = 10;
            for (int i = 1; i <= captured.Program.EventCount; i++)
            {
                var entry = captured.Program.Get(i);
                AddFixtureSpawn(timeline, captured.Prefabs[entry.PrefabIndex], entry.ScheduledTime / 15, .02, 1);
            }
            Set(rulesCopy, "timeline", timeline); Set(rulesCopy, "waveDuration", 2f); Set(Spawner, "waveRules", rulesCopy);
            var events = new List<(WaveSpawnOpportunity spawn, uint id, string prefab)>();
            Spawner.WaveEnemySpawned += (spawn, id) =>
            {
                var root = NetworkServer.spawned[id]; var body = root.GetComponent<EnemyController>().collider;
                Physics2D.SyncTransforms(); var bounds = Spawner.BoundaryGround.bounds;
                Assert.That(body.bounds.min.x, Is.GreaterThanOrEqualTo(bounds.min.x - .001));
                Assert.That(body.bounds.max.x, Is.LessThanOrEqualTo(bounds.max.x + .001));
                Assert.That(body.bounds.min.y, Is.GreaterThanOrEqualTo(bounds.min.y - .001));
                Assert.That(body.bounds.max.y, Is.LessThanOrEqualTo(bounds.max.y + .001));
                events.Add((spawn, id, root.name.Replace("(Clone)", "")));
            };
            manager.BeginRun();
            yield return WaitFor(() => events.Count >= 24);
            Assert.That(Spawner.CountCanonicalEnemies(), Is.EqualTo(24), "Changing waves must preserve earlier survivors.");
            Assert.That(events.Select(e => e.prefab).Distinct().Count(), Is.EqualTo(4));
            yield return WaitFor(() => events.Count >= 30);
            foreach (var e in events.Take(6)) NetworkServer.Destroy(NetworkServer.spawned[e.id].gameObject);
            yield return WaitFor(() => events.Count >= 36);
            Assert.That(events.Skip(30).All(e => e.prefab == "NetworkEnemyLustSinner" && e.spawn.Wave == 6), Is.True);
            Assert.That(events.Select(e => e.spawn.Sequence).Distinct().Count(), Is.EqualTo(36));
            Assert.That(Spawner.ServerProgress.TotalSkipped, Is.Zero);
            for (int wave = 2; wave <= 4; wave++)
                Assert.That(events.Where(e => e.spawn.Wave == wave).GroupBy(e => e.prefab).Select(g => g.Count()), Is.EquivalentTo(new[] { 3, 3 }));
        }

        [UnityTest]
        public IEnumerator TimelineRejectsUnregisteredEnemyAndCapsSimultaneousSpawns()
        {
            yield return StartHost(); UseFastRules(1.5f, .2f, 2);
            var timeline = rulesCopy.Timeline;
            var unregistered = Object.Instantiate(Spawner.EnemyPrefab); unregistered.SetActive(false); timelineAssets.Add(unregistered);
            var spawn = (NetworkEnemySpawnClip)timeline.GetOutputTracks().Single().GetClips().Single().asset;
            spawn.enemyPrefab = unregistered;
            Assert.That(manager.TryBeginRun(out _), Is.False);
            spawn.enemyPrefab = Spawner.EnemyPrefab;
            // Three simultaneous authored events, with only two available capacity slots.
            spawn.count = 1;
            AddFixtureSpawn(timeline, Spawner.EnemyPrefab, 0, .2, 1);
            AddFixtureSpawn(timeline, Spawner.EnemyPrefab, 0, .2, 1);
            Assert.That(manager.TryBeginRun(out _), Is.True);
            yield return WaitFor(() => Spawner.ServerProgress.TotalSpawned == 2);
            Assert.That(Spawner.ServerProgress.TotalSkipped, Is.EqualTo(1));
            Assert.That(Spawner.CountCanonicalEnemies(), Is.EqualTo(2));
        }
    }
}
