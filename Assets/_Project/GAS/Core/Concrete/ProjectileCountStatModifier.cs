using System;

namespace MonsterSupergroup.GAS
{
    [Serializable]
    public sealed class ProjectileCountStatModifierParameters : EquipmentModifierParameters
    {
        public ProjectileCountStatModifierParameters()
        {
        }

        public ProjectileCountStatModifierParameters(int countIncrement)
        {
            this.countIncrement = countIncrement;
        }

        public int countIncrement;
        public int CountIncrement => countIncrement;
    }

    [EquipmentModifierType(
        ModifierIdValue,
        "Projectile Count",
        typeof(ProjectileCountStatModifierParameters))]
    public sealed class ProjectileCountStatModifier : StaticStatModifier
    {
        public const uint ModifierIdValue = 0x01000007u;

        public ProjectileCountStatModifier(ProjectileCountStatModifierParameters parameters)
            : base(
                new EquipmentModifierID(ModifierIdValue),
                parameters ?? throw new ArgumentNullException(nameof(parameters)))
        {
        }

        public override void Apply(AttackStatsMultipliers multipliers)
        {
            if (multipliers == null) throw new ArgumentNullException(nameof(multipliers));
            multipliers.projectileCountIncrement +=
                ((ProjectileCountStatModifierParameters)Parameters).countIncrement;
        }
    }
}
