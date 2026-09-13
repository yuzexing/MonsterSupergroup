using System;

namespace MonsterSupergroup.NetworkCombat
{
    public enum EnemyProjectileEndReason : byte { Hit, Expired, Despawned }
    [Serializable]
    public struct EnemyProjectileTermination
    {
        public EnemyProjectileKey Key;
        public EnemyProjectileEndReason Reason;
        public bool IsValid => Key.EnemyEntityId != 0 && Key.ActionId != 0 && Key.ProjectileIndex == 0 && (byte)Reason <= (byte)EnemyProjectileEndReason.Despawned;
    }
}
