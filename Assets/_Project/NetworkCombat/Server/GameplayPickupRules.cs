using System;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [CreateAssetMenu(menuName = "Network Combat/Pickup Rules")]
    public sealed class GameplayPickupRules : ScriptableObject
    {
        public GameplayExperienceRules experience;
        public GameplayPickupDefinition experienceItem;
        public GameplayPickupDefinition healthItem;
        public float itemWeight = .001f;
        public float lowHealthBias = .1f;
        public int randomSeed = 14303;
    }

    public readonly struct PickupDropDecision
    {
        public readonly PickupEffect Effect;
        public readonly string Reason;
        public readonly float Probability, Roll;
        public PickupDropDecision(PickupEffect effect, string reason, float probability = 0, float roll = -1)
        { Effect = effect; Reason = reason; Probability = probability; Roll = roll; }
    }

    public sealed class PickupDropSchedule
    {
        private readonly ExperienceDropSchedule xp;
        private readonly float weight, bias;
        public float XpAccumulator => xp.Accumulator;
        public float ItemAccumulator { get; private set; }
        public PickupDropSchedule(ExperienceParameters experience, float itemWeight, float lowHealthBias)
        {
            if (!ExperienceParameters.Finite(itemWeight) || itemWeight < 0 ||
                !ExperienceParameters.Finite(lowHealthBias) || lowHealthBias < 0)
                throw new ArgumentException("Invalid pickup drop weights.");
            xp = new ExperienceDropSchedule(experience); weight = itemWeight; bias = lowHealthBias;
        }
        public PickupDropDecision Consume(uint enemy, uint deathVersion, float amount, float healthFraction,
            int itemCount, int itemLimit, Func<float> random)
        {
            bool selectedXp = xp.ConsumeDeath(enemy, deathVersion, amount, out string reason);
            if (reason == "duplicate-death" || reason == "invalid-death")
                return new PickupDropDecision(PickupEffect.None, reason);
            ItemAccumulator += weight;
            if (selectedXp) return new PickupDropDecision(PickupEffect.Experience, "xp");
            if (reason != "accumulating") return new PickupDropDecision(PickupEffect.None, reason);
            float probability = ItemAccumulator + (ExperienceParameters.Finite(healthFraction)
                ? bias * (1 - Mathf.Clamp01(healthFraction)) : 0);
            float roll = random();
            if (roll > probability) return new PickupDropDecision(PickupEffect.None, "item-miss", probability, roll);
            ItemAccumulator = 0;
            if (itemLimit > 0 && itemCount >= itemLimit)
                return new PickupDropDecision(PickupEffect.None, "item-cap", probability, roll);
            return new PickupDropDecision(PickupEffect.RestoreHealth, "health", probability, roll);
        }
    }
}
