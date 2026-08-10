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
            DamageType damageType)
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
    }
}
