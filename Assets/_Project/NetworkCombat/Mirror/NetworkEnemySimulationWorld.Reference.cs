using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkEnemySimulationWorld
    {
        [Server]
        internal bool RepositionReferenceEnemy(NetworkEnemySimulationAgent enemy, Vector2 position)
        {
            if (enemy == null || !enemy.Birth.Enabled || !IsServerEnemyAlive(enemy.netId) ||
                !Registry.TryGetLatestSnapshot(enemy.netId, out var pose)) return false;
            var assignment = Registry.RenewAssignment(enemy.netId);
            pose.Position = position; pose.Velocity = Vector2.zero; pose.Sequence = 0;
            pose.AssignmentEpoch = assignment.Epoch; pose.SampleNetworkTime = NetworkTime.time;
            pose.Flags = EnemySimulationSnapshotFlags.Discontinuity;
            if (enemy.Birth.ResetOnReposition)
            {
                pose.Runtime = default;
                pendingServerKnockbacks.Remove(enemy.netId);
                NetworkCombatWorld.Instance.ResetReferenceEnemy(enemy);
            }
            var checkpoint = new EnemySimulationCheckpoint { Movement = pose };
            Registry.RecordCheckpoint(checkpoint);
            GetHandoff(enemy.netId).Begin(assignment.Epoch, assignment.AggroTargetPlayerId,
                assignment.Host == EnemySimulationHost.ClientPlayer, NetworkTime.time);
            enemy.SetServerHandoff(new EnemySimulationHandoff { Assignment = assignment, Checkpoint = checkpoint,
                Reason = EnemyTargetChangeReason.ReferenceReposition, CommittedAt = NetworkTime.time });
            return true;
        }
    }
}
