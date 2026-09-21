using System;
using System.Collections.Generic;
using System.Linq;
using MonsterSupergroup.Gameplay.Combat.Content;
using UnityEngine;
using UnityEngine.Timeline;

namespace MonsterSupergroup.NetworkCombat
{
    public static partial class NetworkWaveTimelineCompiler
    {
        public static WaveSpawnProgram Compile(TimelineAsset timeline, double waveDuration, out GameObject[] prefabs) =>
            Compile(timeline, waveDuration, out prefabs, out _);
        public static WaveSpawnProgram Compile(TimelineAsset timeline, double waveDuration, out GameObject[] prefabs,
            out EnemyDefinitionSnapshot[] capturedDefinitions)
        {
            if (timeline == null) throw new ArgumentException("Wave Timeline is missing.");
            if (timeline.durationMode != TimelineAsset.DurationMode.FixedLength)
                throw new ArgumentException("Wave Timeline must use Fixed Length so the last complete wave can repeat.");
            var bodies = new List<GameObject>();
            var definitions = new List<EnemyDefinitionSnapshot>();
            var sources = new Dictionary<Guid, EnemyDefinition>();
            var ids = new Dictionary<Guid, int>();
            var events = new List<WaveSpawnEntry>();
            foreach (var root in timeline.GetRootTracks()) Collect(root);
            var program = new WaveSpawnProgram(waveDuration, timeline.duration, events.OrderBy(e => e.Time));
            prefabs = bodies.ToArray(); capturedDefinitions = definitions.ToArray(); return program;

            void Collect(TrackAsset track)
            {
                if (track.muted) return;
                if (track is NetworkEnemySpawnTrack)
                    foreach (var clip in track.GetClips())
                    {
                        if (!(clip.asset is NetworkEnemySpawnClip spawn))
                            throw new ArgumentException("Invalid enemy clip in " + track.name + "/" + clip.displayName);
                        if (spawn.count < 1 || spawn.count > 10000 || !Finite(clip.start) || clip.start < 0 ||
                            !Finite(clip.duration) || clip.duration <= 0 || clip.end > timeline.duration + WaveSpawnProgram.Epsilon ||
                            clip.clipIn != 0 || clip.timeScale != 1)
                            throw new ArgumentException("Invalid spawn time/count or unsupported clip-in/time scale: " + clip.displayName);
                        EnemyDefinitionSnapshot definition = null;
                        GameObject prefab;
                        if (spawn.AuthoringVersion == 1)
                        {
                            if (spawn.Enemy == null) throw new ArgumentException("Choose an Enemy Definition in " + track.name + "/" + clip.displayName);
                            definition = spawn.Enemy.Capture(); prefab = definition.Prefab;
                        }
                        else if (spawn.AuthoringVersion == 0) prefab = spawn.enemyPrefab; // Explicit unmigrated schema.
                        else throw new ArgumentException("Unsupported enemy clip schema: " + spawn.AuthoringVersion);
                        if (prefab == null) throw new ArgumentException("Missing enemy Prefab in " + track.name + "/" + clip.displayName);
                        int prefabIndex = bodies.IndexOf(prefab);
                        if (prefabIndex < 0) { prefabIndex = bodies.Count; bodies.Add(prefab); }
                        int definitionIndex = -1;
                        if (definition != null)
                        {
                            if (sources.TryGetValue(definition.Id, out var original) && original != spawn.Enemy)
                                throw new ArgumentException("Duplicate DefinitionId in Timeline: " + definition.Id);
                            if (!ids.TryGetValue(definition.Id, out definitionIndex))
                            {
                                definitionIndex = definitions.Count; ids.Add(definition.Id, definitionIndex);
                                sources.Add(definition.Id, spawn.Enemy); definitions.Add(definition);
                            }
                        }
                        if (events.Count + spawn.count > 100000) throw new ArgumentException("Timeline exceeds 100000 spawn events.");
                        for (int i = 0; i < spawn.count; i++)
                            events.Add(new WaveSpawnEntry(clip.start + clip.duration * i / spawn.count, prefabIndex, definitionIndex));
                    }
                else if (!(track is GroupTrack)) throw new ArgumentException("Unsupported wave Timeline track: " + track.name);
                foreach (var child in track.GetChildTracks()) Collect(child);
            }
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
