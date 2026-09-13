using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkEnemySimulationWorld
    {
        internal static uint CurrentRound => NetworkManager.singleton is BootGameplayNetworkManager manager && manager.UsePreparationRoom
            ? (NetworkServer.active ? manager.Session.Round : manager.RoomSnapshot.Round) : 0;
        [Server]
        public void StopRunSimulation()
        {
            foreach (var enemy in enemies.Values)
                if (enemy != null) enemy.StopForRunEnd();
            handoffs.Clear(); pendingServerKnockbacks.Clear();
            ClearEnemyProjectiles();
        }
        public void StopClientRun() => ClearClientWaitingState();
        public void ResetClientRound()
        {
            ClearClientWaitingState(); ClearUltimateKnockbackState();
            snapshotBuffer.Clear(); attackPresentationBuffer.Clear();
        }
        [Server]
        public void ResetServerRound()
        {
            ResetClientRound();
            acceptedProjectiles.Clear(); acceptedTerminations.Clear(); pendingServerTerminations.Clear();
            handoffs.Clear(); pendingServerKnockbacks.Clear();
            players.Clear(); enemies.Clear(); neverAssignedEnemies.Clear(); enemyIdBuffer.Clear();
            AcceptedProjectileCount = AcceptedProjectileTerminationCount = 0;
            RoutedUltimateKnockbackCount = RoutedOrdinaryKnockbackCount = RejectedOrdinaryKnockbackCount = 0;
            Registry = new ServerEnemySimulationRegistry(); nextServerSnapshotTime = NetworkTime.time;
        }
    }
}
