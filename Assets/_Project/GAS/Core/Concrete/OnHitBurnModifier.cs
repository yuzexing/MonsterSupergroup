using System;

namespace MonsterSupergroup.GAS
{
    [Serializable]
    public sealed class OnHitBurnModifierParameters : EquipmentModifierParameters
    {
        public OnHitBurnModifierParameters()
        {
        }

        public OnHitBurnModifierParameters(
            float chance,
            float damageMultiplier,
            int numberOfHits,
            float hitIntervalDuration)
        {
            this.chance = chance;
            this.damageMultiplier = damageMultiplier;
            this.numberOfHits = numberOfHits;
            this.hitIntervalDuration = hitIntervalDuration;
        }

        public float chance;
        public float damageMultiplier;
        public int numberOfHits;
        public float hitIntervalDuration;

        public float Chance => chance;

        public float DamageMultiplier => damageMultiplier;

        public int NumberOfHits => numberOfHits;

        public float HitIntervalDuration => hitIntervalDuration;
    }

    [EquipmentModifierType(
        ModifierIdValue,
        "On Hit Burn",
        typeof(OnHitBurnModifierParameters))]
    public sealed class OnHitBurnModifier : OnHitModifier
    {
        public const uint ModifierIdValue = 0x02000001u;

        public static readonly StatusDefinition BurnDefinition =
            new StatusDefinition(EnemyStatusID.Burn, StatusStackMode.HighestPriority, 1);

        private readonly float chance;
        private readonly float damageMultiplier;
        private readonly int numberOfHits;
        private readonly float hitIntervalDuration;

        public OnHitBurnModifier(OnHitBurnModifierParameters parameters)
            : base(new EquipmentModifierID(ModifierIdValue), Validate(parameters))
        {
            chance = parameters.chance;
            damageMultiplier = parameters.damageMultiplier;
            numberOfHits = parameters.numberOfHits;
            hitIntervalDuration = parameters.hitIntervalDuration;
        }

        public override float GetRollChance()
        {
            return chance;
        }

        public override float GetRollPriority()
        {
            return damageMultiplier * numberOfHits;
        }

        protected override void ApplyEffect(OnHitModifierArgs args)
        {
            if (!args.Target.IsAlive)
            {
                return;
            }

            float burnFactor = SignedMultiplier(args.BurnDamageMultiplier);
            int tickDamage = (int)(args.DamageInfo.Value * damageMultiplier * burnFactor);
            float priority = (float)tickDamage * numberOfHits;
            args.Target.ApplyStatus(new StatusApplication(
                BurnDefinition,
                tickDamage,
                numberOfHits,
                hitIntervalDuration,
                priority,
                args.DamageInfo.Id));
        }

        private static float SignedMultiplier(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Burn multiplier must be finite.");
            }

            return value >= 0f ? 1f + value : 1f / (1f + Math.Abs(value));
        }

        private static OnHitBurnModifierParameters Validate(OnHitBurnModifierParameters parameters)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            if (float.IsNaN(parameters.chance) || float.IsInfinity(parameters.chance))
            {
                throw new ArgumentOutOfRangeException(nameof(parameters), "Chance must be finite.");
            }

            if (float.IsNaN(parameters.damageMultiplier) ||
                float.IsInfinity(parameters.damageMultiplier) ||
                parameters.damageMultiplier < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(parameters),
                    "Burn damage multiplier must be finite and non-negative.");
            }

            if (parameters.numberOfHits < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(parameters), "Burn must tick at least once.");
            }

            if (float.IsNaN(parameters.hitIntervalDuration) ||
                float.IsInfinity(parameters.hitIntervalDuration) ||
                parameters.hitIntervalDuration <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(parameters),
                    "Burn tick interval must be finite and greater than zero.");
            }

            return parameters;
        }
    }
}
