namespace MonsterSupergroup.GAS
{
    public static class DamageTypeUtility
    {
        public static DamageType FromStatus(EnemyStatusID status) => status switch
        {
            EnemyStatusID.Burn => DamageType.Fire,
            EnemyStatusID.Poison => DamageType.Poison,
            EnemyStatusID.Bleed => DamageType.Bleed,
            _ => DamageType.Normal
        };
    }

    public enum DamageType
    {
        Normal = 0,
        Fire = 1,
        Poison = 2,
        Bleed = 3,
        Thorns = 4,
        Projectile = 5,
        Lightning = 6
    }
}
