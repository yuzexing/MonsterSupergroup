using System.Collections.Generic;
using Mirror;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkEnemySimulationWorld
    {
        private readonly Dictionary<uint, List<EnemyKnockbackCommand>> pendingServerKnockbacks =
            new Dictionary<uint, List<EnemyKnockbackCommand>>();

        internal void NotifyRuntimeChanged(NetworkEnemySimulationAgent enemy)
        {
            if (enemy == null || !enemy.Authority.RunsNavigation) return;
            var checkpoint = enemy.CaptureCurrentCheckpoint();
            if (isServer && enemy.Assignment.Host != EnemySimulationHost.ClientPlayer)
            {
                Registry.RecordCheckpoint(checkpoint);
                AcknowledgeKnockback(checkpoint.Movement);
            }
            else NetworkClient.localPlayer?.GetComponent<NetworkEnemySimulationEndpoint>()?.CmdSubmitRuntimeCheckpoint(checkpoint);
        }

        internal void SubmitRuntimeCheckpoint(NetworkEnemySimulationEndpoint endpoint, EnemySimulationCheckpoint checkpoint)
        {
            var pose = checkpoint.Movement;
            if (!isServer || endpoint == null || !players.TryGetValue(endpoint.netId, out var registered) || registered != endpoint ||
                !IsServerEnemyAlive(pose.EnemyEntityId) || !pose.IsFinite ||
                !Registry.TryGetAssignment(pose.EnemyEntityId, out var assignment) || assignment.Host != EnemySimulationHost.ClientPlayer ||
                assignment.SimulationOwnerPlayerId != endpoint.netId || assignment.Epoch != pose.AssignmentEpoch) return;
            if(!enemies.TryGetValue(pose.EnemyEntityId,out var enemy)||enemy==null||!enemy.ValidateSequenceAction(pose.Runtime.Action))return;
            Registry.RecordCheckpoint(checkpoint);
            AcknowledgeKnockback(pose);
        }

        private void AcknowledgeKnockback(EnemySimulationSnapshot snapshot)
        {
            if (!pendingServerKnockbacks.TryGetValue(snapshot.EnemyEntityId, out var pending)) return;
            ulong handled = snapshot.Runtime.LastHandledKnockbackId;
            pending.RemoveAll(command => command.CommandId <= handled || !command.IsTimely(NetworkTime.time));
            if (pending.Count == 0) pendingServerKnockbacks.Remove(snapshot.EnemyEntityId);
        }

        private void RouteKnockback(NetworkEnemySimulationAgent enemy, EnemyKnockbackCommand command, bool remember = true)
        {
            if (remember)
            {
                if (!pendingServerKnockbacks.TryGetValue(enemy.netId, out var pending))
                    pendingServerKnockbacks.Add(enemy.netId, pending = new List<EnemyKnockbackCommand>());
                pending.RemoveAll(value => !value.IsTimely(NetworkTime.time));
                if (pending.Count >= 32) return;
                pending.Add(command);
            }
            command.AssignmentEpoch = enemy.Assignment.Epoch;
            if (enemy.Assignment.Host == EnemySimulationHost.ClientPlayer)
            {
                if (TryGetEligiblePlayer(enemy.Assignment.SimulationOwnerPlayerId, out var endpoint))
                    endpoint.TargetApplyKnockback(endpoint.connectionToClient, command);
            }
            else if (enemy.Assignment.Host != EnemySimulationHost.Frozen)
            {
                enemy.TryApplyKnockback(command, 0, true);
                NotifyRuntimeChanged(enemy);
            }
            BroadcastAcceptedSequenceInterrupt(enemy, command);
        }

        private void ReroutePendingKnockback(NetworkEnemySimulationAgent enemy)
        {
            if (!pendingServerKnockbacks.TryGetValue(enemy.netId, out var pending)) return;
            pending.RemoveAll(command => !command.IsTimely(NetworkTime.time));
            foreach (var command in pending.ToArray()) RouteKnockback(enemy, command, false);
        }
    }
}
