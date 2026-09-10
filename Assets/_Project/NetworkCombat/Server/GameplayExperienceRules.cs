using System;
using System.Collections.Generic;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [CreateAssetMenu(menuName = "Network Combat/Gameplay Experience Rules")]
    public sealed class GameplayExperienceRules : ScriptableObject
    {
        [SerializeField] private AnimationCurve xpCurve;
        [SerializeField] private float initialAccumulator = 2f;
        [SerializeField] private float killWeight = 0.5f;

        public bool TryCapture(out ExperienceParameters parameters, out string error)
        {
            try
            {
                parameters = new ExperienceParameters(xpCurve, initialAccumulator, killWeight);
                error = null; return true;
            }
            catch (ArgumentException exception) { parameters = null; error = exception.Message; return false; }
        }
    }

    /// <summary>Immutable run values, including all finite curve levels. No asset reads after capture.</summary>
    public sealed class ExperienceParameters
    {
        private readonly int[] thresholds = new int[99];
        public float InitialAccumulator { get; }
        public float KillWeight { get; }

        public ExperienceParameters(AnimationCurve curve, float initialAccumulator, float killWeight)
        {
            if (curve == null || curve.length < 2 || !Finite(initialAccumulator) || initialAccumulator < 0 ||
                !Finite(killWeight) || killWeight < 0 || killWeight > 1)
                throw new ArgumentException("XP curve or drop accumulator configuration is invalid.");
            for (int i = 1; i <= 99; i++)
            {
                float value = curve.Evaluate(i / 100f) * 1000f;
                if (!Finite(value) || value < 1 || value > int.MaxValue)
                    throw new ArgumentException($"XP threshold is invalid at level {i}.");
                thresholds[i - 1] = (int)value;
            }
            InitialAccumulator = initialAccumulator;
            KillWeight = killWeight;
        }

        public int Threshold(int level) => level < 1 ? throw new ArgumentOutOfRangeException(nameof(level))
            : level >= 100 ? 1000 : thresholds[level - 1];
        public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public bool TryAdvance(int level, float experience, float amount, out int newLevel, out float remainder)
        {
            newLevel = level; remainder = experience;
            if (level < 1 || !Finite(experience) || experience < 0 || experience >= Threshold(level) ||
                !Finite(amount) || amount <= 0) return false;
            double total = (double)experience + amount;
            while (newLevel < 100 && total >= Threshold(newLevel)) total -= Threshold(newLevel++);
            if (newLevel >= 100)
            {
                double levels = Math.Floor(total / 1000);
                if (levels > int.MaxValue - newLevel) return false;
                newLevel += (int)levels;
                total -= levels * 1000;
            }
            remainder = (float)total;
            return true;
        }
    }

    /// <summary>Run-scoped death identities; an AoE cause may legitimately appear for several enemies.</summary>
    public sealed class ExperienceDropSchedule
    {
        private readonly HashSet<ulong> deaths = new HashSet<ulong>();
        private readonly ExperienceParameters parameters;
        public float Accumulator { get; private set; }
        public ExperienceDropSchedule(ExperienceParameters parameters)
        {
            this.parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            Accumulator = parameters.InitialAccumulator;
        }
        public bool ConsumeDeath(uint enemyId, uint deathVersion, float rawExperience, out string reason)
        {
            if (enemyId == 0 || deathVersion == 0) { reason = "invalid-death"; return false; }
            if (!deaths.Add(((ulong)enemyId << 32) | deathVersion)) { reason = "duplicate-death"; return false; }
            Accumulator += parameters.KillWeight;
            if (Accumulator < 1) { reason = "accumulating"; return false; }
            Accumulator -= 1;
            if (!ExperienceParameters.Finite(rawExperience) || rawExperience <= 0)
            { reason = "nonpositive-xp"; return false; }
            reason = "drop"; return true;
        }
    }
}
