using System;

namespace MonsterSupergroup.Gameplay.Combat
{
    /// <summary>Transport data using the same absolute session clock as the dash runtime.</summary>
    [Serializable]
    public struct PlayerDashSnapshot
    {
        public int MaxCharges;
        public double NextUseAt;
        public double[] RechargeReadyAt;
    }
}
