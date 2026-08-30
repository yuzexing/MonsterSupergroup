namespace MonsterSupergroup.GAS
{
    public readonly struct AttackStatsSnapshot
    {
        public AttackStatsSnapshot(
            float damageBeforeRounding,
            int damage,
            int criticalDamage,
            float critRate,
            float critDamageMultiplier,
            float speed,
            float size,
            float duration,
            int projectileCount,
            float knockbackDistance,
            DamageType damageType,
            float damageMultiplierSum,
            float speedMultiplierSum,
            float speedMultipliersProduct,
            float sizeMultiplierSum,
            float durationMultiplierSum,
            float critRateMultiplierSum,
            float critDamageMultiplierSum,
            float knockbackMultiplierSum,
            int baseProjectileCount)
        {
            DamageBeforeRounding = damageBeforeRounding;
            Damage = damage;
            CriticalDamage = criticalDamage;
            CritRate = critRate;
            CritDamageMultiplier = critDamageMultiplier;
            Speed = speed;
            Size = size;
            Duration = duration;
            ProjectileCount = projectileCount;
            KnockbackDistance = knockbackDistance;
            DamageType = damageType;
            DamageMultiplierSum = damageMultiplierSum;
            SpeedMultiplierSum = speedMultiplierSum;
            SpeedMultipliersProduct = speedMultipliersProduct;
            SizeMultiplierSum = sizeMultiplierSum;
            DurationMultiplierSum = durationMultiplierSum;
            CritRateMultiplierSum = critRateMultiplierSum;
            CritDamageMultiplierSum = critDamageMultiplierSum;
            KnockbackMultiplierSum = knockbackMultiplierSum;
            BaseProjectileCount = baseProjectileCount;
        }

        public float DamageBeforeRounding { get; }

        public int Damage { get; }

        public int CriticalDamage { get; }

        public float CritRate { get; }

        public float CritDamageMultiplier { get; }

        public float Speed { get; }

        public float Size { get; }

        public float Duration { get; }

        public int ProjectileCount { get; }

        public float KnockbackDistance { get; }

        public DamageType DamageType { get; }

        /// <summary>
        /// Frozen authored contributions used by HellMaiden presentation scalers.
        /// Gameplay resolution uses the final values above and never recalculates
        /// these contributions when a projectile hits.
        /// </summary>
        public float DamageMultiplierSum { get; }
        public float SpeedMultiplierSum { get; }
        public float SpeedMultipliersProduct { get; }
        public float SizeMultiplierSum { get; }
        public float DurationMultiplierSum { get; }
        public float CritRateMultiplierSum { get; }
        public float CritDamageMultiplierSum { get; }
        public float KnockbackMultiplierSum { get; }
        public int BaseProjectileCount { get; }
    }
}
