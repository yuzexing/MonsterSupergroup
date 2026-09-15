using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.AI.Enemy;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public enum ReferenceSpawnMode : byte { None, CurveBudget, AliveTarget, FormationBurst }
    public enum ReferenceEnemyReadiness : byte { Ready, EvidenceMissing, ImplementationPending, ValidationPending }

    /// <summary>Captured authoring data, not an enemy registry or a second combat state.</summary>
    public sealed class ReferenceSpawnDefinition
    {
        public string Name, SourceEnemy, SourceLocation, MissingEvidence;
        public ReferenceEnemyReadiness Readiness;
        public ReferenceEnemyReadiness SpawnReadiness;
        public string SpawnReadinessNote;
        public int Variant, PrefabIndex, Count;
        public double Start, End;
        public ReferenceSpawnMode Mode;
        public float Cooldown, ContactRadius;
        public Vector2 SpeedMultipliers;
        public bool ExpiresOffscreen, ResetOnReposition;
        public EnemyStats Stats;
        public float[] Timestamps;
    }

    [Serializable]
    public struct ReferenceBarrierDefinition
    {
        public double start, end;
        public float minimumRadius, maximumRadius, shrinkDuration;
        public int sides;
        // An explicit version prevents old serialized structs (enum value 0) from becoming Ready.
        public int lifecycleVersion;
        public ReferenceEnemyReadiness readiness;
        public string readinessNote;
    }

    public sealed class ReferenceWaveProgram
    {
        public readonly ReferenceSpawnDefinition[] Clips;
        public readonly ReferenceBarrierDefinition[] Barriers;
        public readonly double EndTime, SourceDuration;
        public readonly AnimationCurve XpCurve;
        public readonly float XpAmplitude;
        public readonly int Seed;
        public readonly bool ValidationOnly;
        public readonly float OffscreenDistance, RepositionDistance, RepositionGrace, OffscreenTimeout, MaximumDistance;
        public readonly float BurstRadius, BurstAspect, EffectsDelay, ActivationDelay;

        public ReferenceWaveProgram(ReferenceSpawnDefinition[] clips, ReferenceBarrierDefinition[] barriers,
            double endTime, double sourceDuration, AnimationCurve xpCurve, float xpAmplitude, int seed,
            float offscreenDistance, float repositionDistance, float repositionGrace, float offscreenTimeout,
            float maximumDistance, float burstRadius, float burstAspect, float effectsDelay, float activationDelay, bool validationOnly = false)
        {
            Clips = clips; Barriers = (ReferenceBarrierDefinition[])barriers.Clone(); EndTime = endTime;
            ValidationOnly = validationOnly;
            SourceDuration = sourceDuration; XpCurve = new AnimationCurve(xpCurve.keys)
                { preWrapMode = xpCurve.preWrapMode, postWrapMode = xpCurve.postWrapMode };
            XpAmplitude = xpAmplitude; Seed = seed; OffscreenDistance = offscreenDistance;
            RepositionDistance = repositionDistance; RepositionGrace = repositionGrace; OffscreenTimeout = offscreenTimeout;
            MaximumDistance = maximumDistance; BurstRadius = burstRadius; BurstAspect = burstAspect;
            EffectsDelay = effectsDelay; ActivationDelay = activationDelay;
        }

        public float XpMultiplier(double birthTime) => 1 + XpAmplitude * XpCurve.Evaluate((float)(birthTime / SourceDuration));

        public bool OffscreenProcessingDue(double bornAt, double now, double since, float minimumDistance) =>
            now - bornAt >= RepositionGrace && minimumDistance > 0 && !float.IsInfinity(minimumDistance) &&
            (now - since >= OffscreenTimeout || minimumDistance >= MaximumDistance);

        public string ReadinessError()
        {
            foreach (var clip in Clips)
                if (clip.Start < EndTime &&
                    (clip.SpawnReadiness == ReferenceEnemyReadiness.EvidenceMissing ||
                     clip.SpawnReadiness == ReferenceEnemyReadiness.ImplementationPending ||
                     clip.SpawnReadiness == ReferenceEnemyReadiness.ValidationPending && !ValidationOnly ||
                     clip.SpawnReadiness == ReferenceEnemyReadiness.Ready && !string.IsNullOrWhiteSpace(clip.SpawnReadinessNote)))
                    return "Reference spawn gate: " + clip.Name + " [" + clip.SpawnReadiness + "]: " + clip.SpawnReadinessNote;
            foreach (var clip in Clips)
                if (clip.Start < EndTime &&
                    (clip.Readiness == ReferenceEnemyReadiness.EvidenceMissing || clip.Readiness == ReferenceEnemyReadiness.ImplementationPending ||
                    clip.Readiness == ReferenceEnemyReadiness.ValidationPending && !ValidationOnly ||
                    clip.Readiness == ReferenceEnemyReadiness.Ready && !string.IsNullOrWhiteSpace(clip.MissingEvidence)))
                    return "Reference enemy gate: " + clip.SourceEnemy + " [" + clip.Readiness + "]: " + clip.MissingEvidence;
            foreach (var barrier in Barriers)
                if (barrier.start < EndTime && (barrier.lifecycleVersion != 1 ||
                    barrier.readiness == ReferenceEnemyReadiness.EvidenceMissing ||
                    barrier.readiness == ReferenceEnemyReadiness.ImplementationPending ||
                    barrier.readiness == ReferenceEnemyReadiness.ValidationPending && !ValidationOnly ||
                    barrier.readiness == ReferenceEnemyReadiness.Ready && !string.IsNullOrWhiteSpace(barrier.readinessNote)))
                    return "Reference barrier gate: " + barrier.start + " [" + barrier.readiness + "]: " +
                        (barrier.lifecycleVersion != 1 ? "Network lifecycle has not been configured." : barrier.readinessNote);
            return null;
        }

        // Match the source's float arithmetic and 100 right-end samples. Do not replace
        // this with count evenly spaced events or an analytic integral of the curve.
        public static float[] SampleBudget(AnimationCurve curve, int count, double duration)
        {
            if (curve == null || count < 1 || count > 10000 || duration <= 0 || double.IsInfinity(duration) || double.IsNaN(duration))
                throw new ArgumentException("Invalid reference curve budget.");
            var cumulative = new float[101];
            float sum = 0;
            for (int i = 1; i <= 100; i++)
            {
                float density = curve.Evaluate(i / 100f);
                if (density < 0 || float.IsNaN(density) || float.IsInfinity(density))
                    throw new ArgumentException("Reference spawn density must be finite and nonnegative.");
                sum += density / 100f; cumulative[i] = sum;
            }
            if (sum <= 0) throw new ArgumentException("Reference curve has no spawn density.");
            for (int i = 0; i <= 100; i++) cumulative[i] *= count / sum;
            var result = new List<float>(count + 1);
            int k = 0;
            for (int i = 1; i <= 100; i++)
                for (; k < cumulative[i]; k++)
                    result.Add(Mathf.Lerp((i - 1) * (float)duration / 100f, i * (float)duration / 100f,
                        (k - cumulative[i - 1]) / (cumulative[i] - cumulative[i - 1])));
            return result.ToArray();
        }
    }
}
