using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Timeline;

namespace MonsterSupergroup.NetworkCombat
{
    public static partial class NetworkWaveTimelineCompiler
    {
        public static WaveSpawnProgram Compile(TimelineAsset timeline, double waveDuration, out GameObject[] prefabs)
        {
            if (timeline == null) throw new ArgumentException("Wave Timeline is missing.");
            if (timeline.durationMode != TimelineAsset.DurationMode.FixedLength)
                throw new ArgumentException("Wave Timeline must use Fixed Length so the last complete wave can repeat.");
            var definitions = new List<GameObject>();
            var events = new List<WaveSpawnEntry>();
            foreach (var root in timeline.GetRootTracks()) Collect(root);
            // LINQ's stable ordering preserves track/clip/event order for simultaneous spawns.
            var program = new WaveSpawnProgram(waveDuration, timeline.duration, events.OrderBy(e => e.Time));
            prefabs = definitions.ToArray();
            return program;

            void Collect(TrackAsset track)
            {
                if (track.muted) return;
                if (track is NetworkEnemySpawnTrack)
                    foreach (var clip in track.GetClips())
                    {
                        if (!(clip.asset is NetworkEnemySpawnClip spawn) || spawn.enemyPrefab == null)
                            throw new ArgumentException("Missing enemy Prefab in " + track.name + "/" + clip.displayName);
                        if (spawn.count < 1 || spawn.count > 10000 || !Finite(clip.start) || clip.start < 0 ||
                            !Finite(clip.duration) || clip.duration <= 0 || clip.end > timeline.duration + WaveSpawnProgram.Epsilon ||
                            clip.clipIn != 0 || clip.timeScale != 1)
                            throw new ArgumentException("Invalid spawn time/count or unsupported clip-in/time scale: " + clip.displayName);
                        int index = definitions.IndexOf(spawn.enemyPrefab);
                        if (index < 0) { index = definitions.Count; definitions.Add(spawn.enemyPrefab); }
                        if (events.Count + spawn.count > 100000) throw new ArgumentException("Timeline exceeds 100000 spawn events.");
                        for (int i = 0; i < spawn.count; i++) events.Add(new WaveSpawnEntry(clip.start + clip.duration * i / spawn.count, index));
                    }
                else if (!(track is GroupTrack)) throw new ArgumentException("Unsupported wave Timeline track: " + track.name);
                foreach (var child in track.GetChildTracks()) Collect(child);
            }
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
