using System;
using MonsterSupergroup.Gameplay.Combat;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>The server's per-equipped-summon cocoon deadline; no pet, AI, or attack instance is persisted.</summary>
    [Serializable]
    public struct PlayerSummonMaturitySnapshot
    {
        public int SlotIndex;
        public uint WeaponId;
        public double MaturityAt;
        public bool IsValid => (uint)SlotIndex < PlayerBuildRuntime.HandSlotCount && WeaponId != 0 &&
            !double.IsNaN(MaturityAt) && !double.IsInfinity(MaturityAt) && MaturityAt >= 0d;
    }
}
