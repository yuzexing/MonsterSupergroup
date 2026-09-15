using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkEnemySimulationWorld
    {
        private readonly Dictionary<(uint Player, ulong Root), uint> appliedUltimatePulses =
            new Dictionary<(uint, ulong), uint>();
        private readonly Dictionary<uint, List<PendingKnockback>> pendingClientKnockbacks = new Dictionary<uint, List<PendingKnockback>>();
        private readonly List<uint> pendingKnockbackIds = new List<uint>();
        private readonly List<(uint Player, ulong Root)> completedKnockbackRoots = new List<(uint, ulong)>();
        private ulong knockbackCommandId;
        public int RoutedUltimateKnockbackCount { get; private set; }
        public int RoutedOrdinaryKnockbackCount { get; private set; }
        public int RejectedOrdinaryKnockbackCount { get; private set; }
        public int PendingClientKnockbackCount => pendingClientKnockbacks.Count;

        // Only the server coordinator calls this after admission. Owner requests contain
        // no radius, direction, curve or damage fields for this startup impulse.
        [Server]
        public int ServerApplyUltimateKnockback(uint sourcePlayerId, ulong admittedRootId, uint abilityCombatId,
            Vector2 origin, float radius, KnockbackSettings settings)
        {
            var combat = NetworkCombatWorld.Instance;
            if (combat == null || (abilityCombatId & 0x80000000u) == 0 ||
                !combat.Gateway.Attacks.Contains(sourcePlayerId, admittedRootId, abilityCombatId) ||
                !combat.Gateway.Ledger.TryGetState(sourcePlayerId, out var player) || !player.Alive)
                return 0;
            if (!EnemyKnockbackSettings.Finite(origin.x) || !EnemyKnockbackSettings.Finite(origin.y) ||
                !EnemyKnockbackSettings.Finite(radius) || radius < 0f || !EnemyKnockbackSettings.Finite(radius * radius))
                throw new ArgumentOutOfRangeException(nameof(radius), "Ultimate impulse geometry must be finite.");
            EnemyKnockbackSettings preset = EnemyKnockbackSettings.From(settings);
            PruneUltimateKnockbackPulses();
            var pulse = (sourcePlayerId, admittedRootId);
            if (appliedUltimatePulses.ContainsKey(pulse)) return 0;
            appliedUltimatePulses.Add(pulse, abilityCombatId);
            int applied = 0;
            foreach (NetworkEnemySimulationAgent enemy in enemies.Values)
            {
                if (enemy == null || !enemy.IsCanonicalAlive ||
                    !combat.Gateway.Ledger.TryGetState(enemy.netId, out var canonical) || !canonical.Alive ||
                    !Registry.TryGetAssignment(enemy.netId, out EnemySimulationAssignment assignment) ||
                    assignment.Host == EnemySimulationHost.Frozen) continue;
                Vector2 position = Registry.TryGetLatestSnapshot(enemy.netId, out EnemySimulationSnapshot snapshot)
                    ? snapshot.Position : (Vector2)enemy.transform.position;
                if ((position - origin).sqrMagnitude > radius * radius) continue;
                if (knockbackCommandId == ulong.MaxValue) throw new InvalidOperationException("Knockback command sequence exhausted.");
                var command = new EnemyKnockbackCommand
                {
                    EnemyEntityId = enemy.netId, AssignmentEpoch = assignment.Epoch, SourcePlayerId = sourcePlayerId,
                    AbilityCombatId = abilityCombatId, RootEventId = admittedRootId, CommandId = ++knockbackCommandId,
                    IssuedAt = EnemySimulationClock.Now, Origin = origin, Settings = preset
                };
                RouteKnockback(enemy, command);
                applied++;
                RoutedUltimateKnockbackCount++;
            }
            return applied;
        }

        [Client]
        internal void ReceiveKnockback(EnemyKnockbackCommand command, uint receivingPlayerId)
        {
#if MONSTER_ENEMY_HANDOFF_VALIDATION
            Debug.Log($"[EnemyHandoffImpulse] enemy={command.EnemyEntityId} valid={command.IsValid} timely={command.IsTimely(EnemySimulationClock.Now)} issued={command.IssuedAt:0.000} now={EnemySimulationClock.Now:0.000} render={NetworkTime.time:0.000}");
#endif
            if (!command.IsValid || !command.IsTimely(EnemySimulationClock.Now) || NetworkClient.localPlayer == null ||
                NetworkClient.localPlayer.netId != receivingPlayerId) return;
            if (enemies.TryGetValue(command.EnemyEntityId, out NetworkEnemySimulationAgent enemy) && enemy != null)
            {
                if (command.AssignmentEpoch == enemy.Assignment.Epoch && enemy.AppliedHandoffEpoch == command.AssignmentEpoch && enemy.ProductEnemyInitialized)
                {
                    enemy.TryApplyKnockback(command, receivingPlayerId, false);
                    NotifyRuntimeChanged(enemy);
                    return;
                }
                if (command.AssignmentEpoch != enemy.Assignment.Epoch &&
                    !EnemySimulationSequence.IsNewer(command.AssignmentEpoch, enemy.Assignment.Epoch)) return;
            }
            // Reliable endpoint RPCs may precede a newly spawned Enemy or its assignment SyncVar.
            // Preserve command order: discarding an earlier impulse can change which
            // overlapping impulse the original movement implementation accepts.
            if (pendingClientKnockbacks.Count >= 4096 && !pendingClientKnockbacks.ContainsKey(command.EnemyEntityId)) return;
            if (!pendingClientKnockbacks.TryGetValue(command.EnemyEntityId, out var pending))
                pendingClientKnockbacks.Add(command.EnemyEntityId, pending = new List<PendingKnockback>());
            pending.RemoveAll(value => !value.Command.IsTimely(EnemySimulationClock.Now) ||
                EnemySimulationSequence.IsNewer(command.AssignmentEpoch, value.Command.AssignmentEpoch));
            if (pending.Exists(value => value.Command.CommandId == command.CommandId) || pending.Count >= 32) return;
            pending.Add(new PendingKnockback(command, receivingPlayerId));
            pending.Sort((left, right) => left.Command.CommandId.CompareTo(right.Command.CommandId));
        }

        private void LateUpdate()
        {
            UpdateReferenceTrapPresentation();
            if (isServer) PruneUltimateKnockbackPulses();
            if (!NetworkClient.active) { pendingClientKnockbacks.Clear(); return; }
            pendingKnockbackIds.Clear();
            pendingKnockbackIds.AddRange(pendingClientKnockbacks.Keys);
            foreach (uint enemyId in pendingKnockbackIds)
            {
                var pending = pendingClientKnockbacks[enemyId];
                for (int i = 0; i < pending.Count;)
                {
                    var item = pending[i];
                    var command = item.Command;
                    if (!command.IsTimely(EnemySimulationClock.Now) || NetworkClient.localPlayer == null ||
                        item.ReceiverPlayerId != NetworkClient.localPlayer.netId)
                    { pending.RemoveAt(i); continue; }
                    if (!enemies.TryGetValue(enemyId, out var enemy) || enemy == null) { i++; continue; }
                    if (command.AssignmentEpoch != enemy.Assignment.Epoch)
                    {
                        if (!EnemySimulationSequence.IsNewer(command.AssignmentEpoch, enemy.Assignment.Epoch)) pending.RemoveAt(i);
                        else i++;
                        continue;
                    }
                    if (!enemy.ProductEnemyInitialized || enemy.AppliedHandoffEpoch != command.AssignmentEpoch) { i++; continue; }
                    pending.RemoveAt(i);
                    enemy.TryApplyKnockback(command, item.ReceiverPlayerId, false);
                    NotifyRuntimeChanged(enemy);
                }
                if (pending.Count == 0) pendingClientKnockbacks.Remove(enemyId);
            }
        }

        private void PruneUltimateKnockbackPulses()
        {
            var combat = NetworkCombatWorld.Instance;
            completedKnockbackRoots.Clear();
            foreach (var pulse in appliedUltimatePulses)
                if (combat == null || !combat.Gateway.Attacks.Contains(pulse.Key.Player, pulse.Key.Root, pulse.Value))
                    completedKnockbackRoots.Add(pulse.Key);
            foreach (var key in completedKnockbackRoots) appliedUltimatePulses.Remove(key);
        }

        internal void ForgetPendingKnockback(uint enemyId) => pendingClientKnockbacks.Remove(enemyId);

        private void HandleAcceptedOrdinaryHit(CombatResult result, CombatApplyResult applied, double serverTime)
        {
            if (!result.Knockback.Requested) return;
            var request = result.Knockback;
            if (!isServer || !applied.State.Alive || applied.AppliedDamage <= 0 ||
                !request.IsValid || !request.IsTimely(serverTime) || result.AbilityId == 0 ||
                ServerStatusDamageAdmissions.IsPeriodic(result) ||
                (result.AbilityId & 0x80000000u) != 0 ||
                !enemies.TryGetValue(result.TargetEntityId, out var enemy) || enemy == null ||
                !Registry.TryGetAssignment(result.TargetEntityId, out var assigned) ||
                EnemySimulationSequence.IsNewer(request.AssignmentEpoch, assigned.Epoch) || assigned.Host == EnemySimulationHost.Frozen ||
                !observedCombatGateway.Attacks.TryGetKnockback(result.SourcePlayerId, result.RootEventId, out var preset))
            { RejectedOrdinaryKnockbackCount++; return; }
            if (knockbackCommandId == ulong.MaxValue) throw new InvalidOperationException("Knockback command sequence exhausted.");
            var command = new EnemyKnockbackCommand
            {
                Kind = EnemyKnockbackKind.OrdinaryHit, EnemyEntityId = enemy.netId,
                AssignmentEpoch = assigned.Epoch, SourcePlayerId = result.SourcePlayerId, AbilityCombatId = result.AbilityId,
                RootEventId = result.RootEventId, DamageEventId = result.EventId, CommandId = ++knockbackCommandId,
                IssuedAt = request.HitNetworkTime, Origin = request.Origin, MultiplierSum = request.MultiplierSum, Settings = preset
            };
            if (!command.IsValid) { RejectedOrdinaryKnockbackCount++; return; }
            RouteKnockback(enemy, command);
            RoutedOrdinaryKnockbackCount++;
        }

        private void ForgetPlayerKnockbackPulses(uint player)
        {
            completedKnockbackRoots.Clear();
            foreach (var key in appliedUltimatePulses.Keys) if (key.Player == player) completedKnockbackRoots.Add(key);
            foreach (var key in completedKnockbackRoots) appliedUltimatePulses.Remove(key);
        }

        private void ClearUltimateKnockbackState()
        {
            appliedUltimatePulses.Clear();
            pendingClientKnockbacks.Clear();
            completedKnockbackRoots.Clear();
            pendingKnockbackIds.Clear();
        }

        private readonly struct PendingKnockback
        {
            public PendingKnockback(EnemyKnockbackCommand command, uint receiver)
            { Command = command; ReceiverPlayerId = receiver; }
            public EnemyKnockbackCommand Command { get; }
            public uint ReceiverPlayerId { get; }
        }
    }
}
