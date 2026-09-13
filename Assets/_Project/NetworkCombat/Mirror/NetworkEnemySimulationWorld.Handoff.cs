using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.AI;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkEnemySimulationWorld
    {
        private readonly Dictionary<uint, EnemyHandoffProgress> handoffs = new Dictionary<uint, EnemyHandoffProgress>();

        public EnemyTargetChangeResult RequestTargetChange(uint enemyId, uint targetAvatarId, EnemyTargetChangeReason reason)
        {
            if (!isServer) return EnemyTargetChangeResult.NotServer;
            if (BootGameplayNetworkManager.CombatHasEnded) return EnemyTargetChangeResult.InvalidTarget;
            if (!enemies.TryGetValue(enemyId, out var enemy) || enemy == null) return EnemyTargetChangeResult.UnknownEnemy;
            if (!IsServerEnemyAlive(enemyId)) return EnemyTargetChangeResult.EnemyDead;
            if (!TryGetEligiblePlayer(targetAvatarId, out _)) return EnemyTargetChangeResult.InvalidTarget;
            var state = GetHandoff(enemyId);
            if (!state.Request(targetAvatarId, enemy.Assignment.AggroTargetPlayerId, reason)) return EnemyTargetChangeResult.Unchanged;
            return EnemyTargetChangeResult.Accepted;
        }

        public bool TryReadHandoff(uint enemyId, out EnemyHandoffDiagnostics diagnostics)
        {
            if (handoffs.TryGetValue(enemyId, out var state)) { diagnostics = state.Diagnostics; return true; }
            diagnostics = default; return false;
        }

        private EnemyHandoffProgress GetHandoff(uint id)
        {
            if (!handoffs.TryGetValue(id, out var state)) handoffs.Add(id, state = new EnemyHandoffProgress());
            return state;
        }

        private bool IsServerEnemyAlive(uint id) => NetworkCombatWorld.Instance != null &&
            NetworkCombatWorld.Instance.Gateway.Ledger.TryGetState(id, out var state) && state.Alive;

        private bool IsEligibleEndpoint(NetworkEnemySimulationEndpoint endpoint)
        {
            if (endpoint == null || !endpoint.isActiveAndEnabled || endpoint.connectionToClient == null ||
                !endpoint.connectionToClient.isReady || endpoint.connectionToClient.identity != endpoint.netIdentity ||
                NetworkCombatWorld.Instance == null ||
                !NetworkCombatWorld.Instance.Gateway.Ledger.TryGetState(endpoint.netId, out var state) || !state.Alive) return false;
            // During AddPlayerForConnection callbacks the restored avatar is not attached yet.
            var session = (NetworkManager.singleton as BootGameplayNetworkManager)?.Session;
            return session == null || (session.TryGetConnection(endpoint.connectionToClient.connectionId, out var participant) &&
                participant.ConnectionState == RunConnectionState.Connected && participant.AvatarId == endpoint.netId);
        }

        private static ulong StableParticipantId(NetworkEnemySimulationEndpoint endpoint)
        {
            ulong id = endpoint.GetComponent<NetworkRunParticipant>()?.ParticipantId ?? 0;
            return id != 0 ? id : endpoint.netId;
        }

        private void UpdateTargetDecisions()
        {
            if (BootGameplayNetworkManager.CombatHasEnded) return;
            double now = NetworkTime.time;
            foreach (var pair in enemies)
            {
                var enemy = pair.Value;
                if (enemy == null || !IsServerEnemyAlive(pair.Key)) { handoffs.Remove(pair.Key); continue; }
                var assigned = enemy.Assignment;
                var progress = GetHandoff(pair.Key);
                if (!TryGetEligiblePlayer(assigned.AggroTargetPlayerId, out _))
                {
                    Vector2 position = Registry.TryGetLatestSnapshot(pair.Key, out var pose) ? pose.Position : (Vector2)enemy.transform.position;
                    var target = FindNearestEligiblePlayer(position);
                    uint targetId = target != null ? target.netId : 0;
                    if (assigned.Host == EnemySimulationHost.Frozen && targetId == 0) continue;
                    var reason = assigned.Host == EnemySimulationHost.Frozen ? EnemyTargetChangeReason.Resume :
                        !players.ContainsKey(assigned.AggroTargetPlayerId) ? EnemyTargetChangeReason.TargetDisconnected :
                        NetworkCombatWorld.Instance.Gateway.Ledger.TryGetState(assigned.AggroTargetPlayerId, out var life) && !life.Alive
                            ? EnemyTargetChangeReason.TargetDowned : EnemyTargetChangeReason.TargetUnavailable;
                    CommitTarget(enemy, targetId, reason);
                    continue;
                }
                if (progress.TimedOut(now))
                {
                    uint pending = progress.HasPending ? progress.PendingTarget : 0;
                    var reason = progress.PendingReason;
                    CommitTarget(enemy, assigned.AggroTargetPlayerId, EnemyTargetChangeReason.Timeout, true);
                    if (pending != 0) progress.Request(pending, assigned.AggroTargetPlayerId, reason);
                    continue;
                }
                if (progress.AwaitingEpoch == 0 && progress.HasPending)
                {
                    uint target = progress.PendingTarget;
                    var reason = progress.PendingReason;
                    if (TryGetEligiblePlayer(target, out _)) CommitTarget(enemy, target, reason);
                    else progress.Begin(assigned.Epoch, assigned.AggroTargetPlayerId, false, now);
                }
            }
        }

        private void CommitTarget(NetworkEnemySimulationAgent enemy, uint target, EnemyTargetChangeReason reason, bool fallback = false)
        {
            var assignment = target == 0 ? Registry.Freeze(enemy.netId) :
                enemy.SimulationMode == EnemySimulationMode.BossServer ? Registry.AssignServerAuthoritative(enemy.netId, target) :
                fallback ? Registry.AssignServerFallback(enemy.netId, target) : Registry.AssignClientOwner(enemy.netId, target, target);
            PublishHandoff(enemy, assignment, reason);
        }

        private void PublishHandoff(NetworkEnemySimulationAgent enemy, EnemySimulationAssignment assignment, EnemyTargetChangeReason reason)
        {
            if (enemy.Assignment.Equals(assignment)) return;
            // A server simulator has a fresher pose than the last scheduled broadcast.
            if (enemy.Authority.RunsNavigation && enemy.TryCaptureSnapshot(NetworkTime.time, out var live))
                Registry.RecordCheckpoint(new EnemySimulationCheckpoint { Movement = live });
            Registry.TryGetLatestSnapshot(enemy.netId, out var snapshot);
            snapshot.AssignmentEpoch = assignment.Epoch;
            snapshot.Sequence = 0;
            snapshot.Flags = EnemySimulationSnapshotFlags.Discontinuity;
            Registry.RecordCheckpoint(new EnemySimulationCheckpoint { Movement = snapshot });
            GetHandoff(enemy.netId).Begin(assignment.Epoch, assignment.AggroTargetPlayerId,
                assignment.Host == EnemySimulationHost.ClientPlayer, NetworkTime.time);
            enemy.SetServerHandoff(new EnemySimulationHandoff
            {
                Assignment = assignment, Checkpoint = new EnemySimulationCheckpoint { Movement = snapshot },
                Reason = reason, CommittedAt = NetworkTime.time
            });
            ReroutePendingKnockback(enemy);
        }

        internal void CollectReadyFallbackEnemies(uint player, List<uint> ids)
        {
            ids.Clear();
            foreach (var pair in enemies)
                if (pair.Value != null && pair.Value.isActiveAndEnabled && pair.Value.ProductEnemyInitialized && pair.Value.IsCanonicalAlive &&
                    pair.Value.AppliedHandoffEpoch == pair.Value.Assignment.Epoch &&
                    pair.Value.Assignment.Host == EnemySimulationHost.ServerFallback && pair.Value.Assignment.AggroTargetPlayerId == player)
                    ids.Add(pair.Key);
        }

        internal void ReportSimulationReady(NetworkEnemySimulationEndpoint endpoint, uint[] ids)
        {
            if (BootGameplayNetworkManager.CombatHasEnded || !isServer || !IsEligibleEndpoint(endpoint) || ids == null || ids.Length > 256) return;
            foreach (uint id in ids)
                if (enemies.TryGetValue(id, out var enemy) && enemy != null && IsServerEnemyAlive(id) &&
                    enemy.Assignment.Host == EnemySimulationHost.ServerFallback && enemy.Assignment.AggroTargetPlayerId == endpoint.netId &&
                    NetworkTime.time - GetHandoff(id).Diagnostics.StartedAt >= .25)
                    CommitTarget(enemy, endpoint.netId, EnemyTargetChangeReason.SimulatorReady);
        }
    }
}
