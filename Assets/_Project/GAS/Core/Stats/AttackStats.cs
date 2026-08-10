using System;

namespace MonsterSupergroup.GAS
{
    [Serializable]
    public struct AttackStats
    {
        public int damage;
        public float critMultiplier;
        public float critRate;
        public float speed;
        public float size;
        public float duration;
        public int projectileCount;
        public float knockbackDistance;
        public DamageType damageType;
    }
}
