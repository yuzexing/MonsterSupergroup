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
        public static ReferenceWaveProgram CompileReference(TimelineAsset timeline, EnemyDatabase database,
            double endTime, double sourceDuration, AnimationCurve xpCurve, float xpAmplitude, int seed,
            float offscreenDistance, float repositionDistance, float repositionGrace, float offscreenTimeout,
            float maximumDistance, float burstRadius, float burstAspect, float effectsDelay, float activationDelay,
            ReferenceBarrierDefinition[] barriers, out GameObject[] prefabs, bool validationOnly = false)
        {
            if (timeline == null || xpCurve == null || !Finite(endTime) || endTime <= 0 ||
                !Finite(sourceDuration) || sourceDuration <= 0 || endTime > sourceDuration ||
                offscreenDistance < 0 || repositionDistance < 0 || repositionGrace < 0 || offscreenTimeout < 0 ||
                maximumDistance <= 0 || burstRadius <= 0 || burstAspect <= 0 || effectsDelay < 0 || activationDelay < 0)
                throw new ArgumentException("Invalid reference stage configuration.");
            var definitions = new List<ReferenceSpawnDefinition>();
            var assets = new List<GameObject>();
            var definitionSources = new Dictionary<Guid, EnemyDefinition>();
            foreach (var root in timeline.GetRootTracks()) Collect(root);
            prefabs = assets.ToArray();
            return new ReferenceWaveProgram(definitions.OrderBy(d => d.Start).ToArray(), barriers ?? Array.Empty<ReferenceBarrierDefinition>(),
                endTime, sourceDuration, xpCurve, xpAmplitude, seed, offscreenDistance, repositionDistance,
                repositionGrace, offscreenTimeout, maximumDistance, burstRadius, burstAspect, effectsDelay, activationDelay, validationOnly);

            void Collect(TrackAsset track)
            {
                if (track.muted) return;
                if (track is NetworkEnemySpawnTrack)
                    foreach (var clip in track.GetClips())
                    {
                        if (!(clip.asset is NetworkEnemySpawnClip spawn) || spawn.referenceMode == ReferenceSpawnMode.None ||
                            !Finite(clip.start) || !Finite(clip.duration) || clip.start < 0 || clip.duration <= 0 ||
                            clip.end > sourceDuration + .001 || clip.clipIn != 0 || clip.timeScale != 1 ||
                            spawn.count < 1 || spawn.count > 10000 || spawn.spawnCooldown < 0 ||
                            spawn.speedMultipliers.x <= 0 || spawn.speedMultipliers.y < spawn.speedMultipliers.x)
                            throw new ArgumentException("Invalid reference clip: " + clip.displayName);
                        EnemyDefinitionSnapshot definition = null;
                        GameObject prefab;
                        AstralShift.HellMaiden.AI.Enemy.EnemyStats stats;
                        if (spawn.AuthoringVersion == 1)
                        {
                            if (spawn.Enemy == null) throw new ArgumentException("Choose an Enemy Definition in " + track.name + "/" + clip.displayName);
                            definition = spawn.Enemy.Capture(); prefab = definition.Prefab; stats = definition.CreateStats();
                            if (definitionSources.TryGetValue(definition.Id, out var original) && original != spawn.Enemy)
                                throw new ArgumentException("Duplicate DefinitionId in reference Timeline: " + definition.Id);
                            definitionSources[definition.Id] = spawn.Enemy;
                        }
                        else if (spawn.AuthoringVersion == 0)
                        {
                            // Versioned migration boundary only, never a missing-definition fallback.
                            var legacy = database != null ? database.GetEnemyData(spawn.sourceEnemy, spawn.sourceVariant) : null;
                            if (legacy == null) throw new ArgumentException("Legacy clip needs its source database until explicitly migrated: " + clip.displayName);
                            prefab = spawn.enemyPrefab; stats = legacy.Stats; stats.Reset();
                        }
                        else throw new ArgumentException("Unsupported enemy clip schema: " + spawn.AuthoringVersion);
                        if (prefab == null && string.IsNullOrWhiteSpace(spawn.missingEvidence))
                            throw new ArgumentException("Missing enemy Prefab: " + clip.displayName);
                        int index = assets.IndexOf(prefab);
                        if (index < 0 && prefab != null) { index = assets.Count; assets.Add(prefab); }
                        definitions.Add(new ReferenceSpawnDefinition {
                            Name = clip.displayName, DefinitionId = definition?.Id ?? Guid.Empty, SourceEnemy = definition?.LegacyEnemyName ?? spawn.sourceEnemy, Variant = definition?.LegacyVariant ?? spawn.sourceVariant,
                            SourceLocation = spawn.sourceLocation, MissingEvidence = spawn.missingEvidence,
                            Readiness = spawn.referenceReadiness,
                            SpawnReadiness = spawn.referenceSpawnReadiness, SpawnReadinessNote = spawn.spawnReadinessNote,
                            PrefabIndex = index, Count = spawn.count, Start = clip.start, End = clip.end, Mode = spawn.referenceMode,
                            Cooldown = spawn.spawnCooldown, ContactRadius = spawn.contactRadius, SpeedMultipliers = spawn.speedMultipliers,
                            ExpiresOffscreen = spawn.expiresOffscreen, ResetOnReposition = spawn.resetOnReposition, Stats = stats,
                            Timestamps = spawn.referenceMode == ReferenceSpawnMode.CurveBudget
                                ? ReferenceWaveProgram.SampleBudget(spawn.spawnCurve, spawn.count, clip.duration) : Array.Empty<float>()
                        });
                    }
                else if (!(track is GroupTrack)) throw new ArgumentException("Unsupported reference track: " + track.name);
                foreach (var child in track.GetChildTracks()) Collect(child);
            }
        }
    }
}
