using Mirror;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>Estimated present server time, independent of the client's delayed render timeline.</summary>
    public static class EnemySimulationClock
    {
        // Mirror's predictedTime targets server arrival time. Remove the estimated
        // one-way transit time to compare action deadlines and server-issued impulses.
        public static double Now => NetworkServer.active ? NetworkTime.time
            : System.Math.Max(0, NetworkTime.predictedTime - NetworkTime.rtt * .5);
        // Action deadlines use gameplay time in reference runs. Snapshot transport/interpolation still uses Now.
        public static double CombatNow => NetworkEnemySimulationWorld.Instance != null
            ? NetworkEnemySimulationWorld.Instance.CombatTime : Now;
    }
}
