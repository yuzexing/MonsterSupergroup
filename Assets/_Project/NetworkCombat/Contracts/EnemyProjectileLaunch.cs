using System;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public enum EnemyProjectileExpiryMode : byte { FixedLifetime, ReferenceOutsideView }
    [Serializable]
    public struct EnemyProjectileKey : IEquatable<EnemyProjectileKey>
    {
        public uint EnemyEntityId;
        public ulong ActionId;
        public ushort ProjectileIndex;
        public bool Equals(EnemyProjectileKey other) => EnemyEntityId == other.EnemyEntityId && ActionId == other.ActionId && ProjectileIndex == other.ProjectileIndex;
        public override bool Equals(object obj) => obj is EnemyProjectileKey other && Equals(other);
        public override int GetHashCode() => unchecked(((int)EnemyEntityId * 397 ^ ActionId.GetHashCode()) * 397 ^ ProjectileIndex);
    }

    /// <summary>One launch, never a projectile position or collision update.</summary>
    [Serializable]
    public struct EnemyProjectileLaunch
    {
        public EnemyProjectileKey Key;
        public uint AssignmentEpoch, EnemyPrefabAssetId;
        public Vector3 Origin;
        public Vector2 Direction;
        public float Speed, Lifetime, StunTime;
        public int Damage;
        public EnemyProjectileExpiryMode ExpiryMode;
        public double FiredAt;
        public uint ViewTargetPlayerId;
        public EnemySimulationCheckpoint Checkpoint;

        public bool IsValid => Key.EnemyEntityId != 0 && Key.ActionId != 0 && Key.ProjectileIndex == 0 &&
            AssignmentEpoch != 0 && EnemyPrefabAssetId != 0 && Finite(Origin.x) && Finite(Origin.y) && Finite(Origin.z) &&
            Finite(Direction.x) && Finite(Direction.y) && Mathf.Abs(Direction.sqrMagnitude - 1) < .001f &&
            Finite(Speed) && Speed > 0 && Finite(Lifetime) && Lifetime > 0 && Damage >= 0 && Finite(StunTime) && StunTime >= 0 &&
            (ExpiryMode == EnemyProjectileExpiryMode.FixedLifetime ||
             ExpiryMode == EnemyProjectileExpiryMode.ReferenceOutsideView && ViewTargetPlayerId != 0 &&
             !double.IsNaN(FiredAt) && !double.IsInfinity(FiredAt) && FiredAt >= 0);
        public Vector3 PositionAt(double combatTime) => Origin + (Vector3)Direction * (Speed * (float)Math.Max(0, combatTime - FiredAt));
        public bool OutsideViewExpired(double combatTime, Bounds view) => combatTime - FiredAt > Lifetime &&
            !view.Contains(new Vector3(PositionAt(combatTime).x, PositionAt(combatTime).y, view.center.z));
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
