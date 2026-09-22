using System;
using System.Collections.Generic;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public enum EnemySnapshotRejectionReason : byte
    {
        None = 0,
        UnknownEnemy = 1,
        WrongHost = 2,
        WrongOwner = 3,
        WrongEpoch = 4,
        StaleSequence = 5,
        InvalidValue = 6,
        StaleTimestamp = 7
    }

    public enum EnemyAttackPresentationRejectionReason : byte
    {
        None = 0,
        UnknownEnemy = 1,
        WrongHost = 2,
        WrongOwner = 3,
        WrongEpoch = 4,
        StaleSequence = 5,
        InvalidValue = 6,
        StaleTimestamp = 7
    }

    /// <summary>
    /// Server-side assignment and latest-snapshot ledger. It deliberately does not
    /// run Enemy AI or recalculate combat.
    /// </summary>
    public sealed partial class ServerEnemySimulationRegistry
    {
        private sealed class Entry
        {
            public EnemySimulationAssignment Assignment;
            public EnemyTargetState Target;
            public EnemySimulationSnapshot LastSnapshot;
            public bool HasSnapshot;
            public ulong LastConfirmedProjectileAction;
            public uint LastAcceptedSequence;
            public double LastAcceptedMovementTime;
            public bool LastSnapshotIsCheckpoint;
            public EnemyAttackPresentationEdge LastAttackPresentation;
            public bool HasAttackPresentation;
            public uint LastAcceptedAttackStateSequence;
        }

        private readonly Dictionary<uint, Entry> entries =
            new Dictionary<uint, Entry>();

        public int Count => entries.Count;

        private void EvidenceCore_RegisterEnemy(
            uint enemyEntityId,
            Vector2 initialPosition,
            double serverTime)
        {
            if (enemyEntityId == 0u)
            {
                throw new ArgumentOutOfRangeException(nameof(enemyEntityId));
            }

            if (entries.ContainsKey(enemyEntityId))
            {
                throw new InvalidOperationException(
                    $"Enemy {enemyEntityId} is already registered for simulation.");
            }

            entries.Add(enemyEntityId, new Entry
            {
                Assignment = new EnemySimulationAssignment
                {
                    EnemyEntityId = enemyEntityId,
                    Host = EnemySimulationHost.Frozen,
                    Epoch = 0u
                },
                LastSnapshot = new EnemySimulationSnapshot
                {
                    EnemyEntityId = enemyEntityId,
                    Position = initialPosition,
                    SampleNetworkTime = serverTime
                },
                HasSnapshot = true
            });
        }

        private void EvidenceCore_UnregisterEnemy(uint enemyEntityId)
        {
            entries.Remove(enemyEntityId);
        }

        private EnemySimulationAssignment EvidenceCore_AssignClientOwner(
            uint enemyEntityId,
            uint ownerPlayerId,
            uint targetPlayerId)
        {
            if (ownerPlayerId == 0u)
            {
                throw new ArgumentOutOfRangeException(nameof(ownerPlayerId));
            }
            if (targetPlayerId == 0u)
            {
                throw new ArgumentOutOfRangeException(nameof(targetPlayerId));
            }

            Entry entry = RequireEntry(enemyEntityId);
            return SetAssignment(
                entry,
                EnemySimulationHost.ClientPlayer,
                ownerPlayerId,
                targetPlayerId);
        }

        private EnemySimulationAssignment EvidenceCore_AssignServerFallback(
            uint enemyEntityId,
            uint targetPlayerId)
        {
            if (targetPlayerId == 0u)
            {
                throw new ArgumentOutOfRangeException(nameof(targetPlayerId));
            }

            Entry entry = RequireEntry(enemyEntityId);
            return SetAssignment(
                entry,
                EnemySimulationHost.ServerFallback,
                0u,
                targetPlayerId);
        }

        private EnemySimulationAssignment EvidenceCore_AssignServerAuthoritative(
            uint enemyEntityId,
            uint targetPlayerId)
        {
            if (targetPlayerId == 0u)
            {
                throw new ArgumentOutOfRangeException(nameof(targetPlayerId));
            }

            Entry entry = RequireEntry(enemyEntityId);
            return SetAssignment(
                entry,
                EnemySimulationHost.ServerAuthoritative,
                0u,
                targetPlayerId);
        }

        private EnemySimulationAssignment EvidenceCore_Freeze(uint enemyEntityId)
        {
            Entry entry = RequireEntry(enemyEntityId);
            return SetAssignment(
                entry,
                EnemySimulationHost.Frozen,
                0u,
                0u);
        }

        public bool TryGetAssignment(
            uint enemyEntityId,
            out EnemySimulationAssignment assignment)
        {
            if (entries.TryGetValue(enemyEntityId, out Entry entry))
            {
                assignment = entry.Assignment;
                return true;
            }

            assignment = default;
            return false;
        }

        public bool TryGetTargetState(uint enemyId, out EnemyTargetState target)
        {
            if (entries.TryGetValue(enemyId, out var entry)) { target = entry.Target; return true; }
            target = default; return false;
        }

        private EnemyTargetState EvidenceCore_SetAggroTarget(uint enemyId, uint playerId, uint controllerPlayerId = 0)
        {
            var entry = RequireEntry(enemyId);
            var target = new EnemyTargetState
            {
                AggroPlayerId = playerId, ControllerPlayerId = controllerPlayerId,
                AllureControlled = controllerPlayerId != 0
            };
            return SetTarget(entry, target);
        }

        private EnemyTargetState EvidenceCore_SetDecoyTarget(uint enemyId, uint owner, ulong cast, Vector2 position, double expires)
        {
            if (owner == 0 || cast == 0 || double.IsNaN(expires) || double.IsInfinity(expires) ||
                float.IsNaN(position.x) || float.IsInfinity(position.x) || float.IsNaN(position.y) || float.IsInfinity(position.y))
                throw new ArgumentException("A valid decoy identity, position and expiry are required.");
            var entry = RequireEntry(enemyId);
            var target = entry.Target;
            target.AllureControlled = true;
            target.DecoyOwnerPlayerId = owner; target.DecoyCastId = cast;
            target.DecoyPosition = position; target.DecoyExpiresAt = expires;
            return SetTarget(entry, target);
        }

        private bool EvidenceCore_ClearDecoyTarget(uint enemyId, uint owner, ulong cast, out EnemyTargetState target)
        {
            var entry = RequireEntry(enemyId);
            target = entry.Target;
            if (!target.MatchesDecoy(owner, cast)) return false;
            target.DecoyOwnerPlayerId = 0; target.DecoyCastId = 0;
            target.DecoyPosition = default; target.DecoyExpiresAt = 0;
            target = SetTarget(entry, target);
            return true;
        }

        private static EnemyTargetState SetTarget(Entry entry, EnemyTargetState target)
        {
            target.Revision = unchecked(entry.Target.Revision + 1u);
            if (target.Revision == 0) target.Revision = 1;
            entry.Target = target;
            entry.Assignment.AggroTargetPlayerId = target.AggroPlayerId;
            return target;
        }

        public bool TryGetLatestSnapshot(
            uint enemyEntityId,
            out EnemySimulationSnapshot snapshot)
        {
            if (entries.TryGetValue(enemyEntityId, out Entry entry) && entry.HasSnapshot)
            {
                snapshot = entry.LastSnapshot;
                snapshot.Runtime.Action.ProjectileEmitted = snapshot.Runtime.Action.ActionId != 0 && snapshot.Runtime.Action.ActionId == entry.LastConfirmedProjectileAction;
                return true;
            }

            snapshot = default;
            return false;
        }

        private void EvidenceCore_ConfirmProjectileLaunch(EnemyProjectileLaunch launch)
        {
            Entry entry = RequireEntry(launch.Key.EnemyEntityId);
            entry.LastConfirmedProjectileAction = Math.Max(entry.LastConfirmedProjectileAction, launch.Key.ActionId);
            RecordCheckpoint(launch.Checkpoint);
        }

        private void EvidenceCore_RecordCheckpoint(EnemySimulationCheckpoint checkpoint)
        {
            Entry entry = RequireEntry(checkpoint.Movement.EnemyEntityId);
            // Reliable action boundaries may overtake an unreliable movement sample.
            if (!entry.HasSnapshot || checkpoint.Movement.SampleNetworkTime >= entry.LastSnapshot.SampleNetworkTime)
            { entry.LastSnapshot = checkpoint.Movement; entry.HasSnapshot = true; entry.LastSnapshotIsCheckpoint = true; }
        }

        public bool TryGetLatestAttackPresentation(
            uint enemyEntityId,
            out EnemyAttackPresentationEdge edge)
        {
            if (entries.TryGetValue(enemyEntityId, out Entry entry) &&
                entry.HasAttackPresentation)
            {
                edge = entry.LastAttackPresentation;
                return true;
            }

            edge = default;
            return false;
        }

        public void GetLatestAttackPresentations(
            List<EnemyAttackPresentationEdge> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            foreach (Entry entry in entries.Values)
            {
                if (entry.HasAttackPresentation)
                {
                    results.Add(entry.LastAttackPresentation);
                }
            }
        }

        private EnemySnapshotRejectionReason EvidenceCore_TryAcceptClientSnapshot(
            uint senderPlayerId,
            EnemySimulationSnapshot snapshot)
        {
            if (!entries.TryGetValue(snapshot.EnemyEntityId, out Entry entry))
            {
                return EnemySnapshotRejectionReason.UnknownEnemy;
            }
            if (entry.Assignment.Host != EnemySimulationHost.ClientPlayer)
            {
                return EnemySnapshotRejectionReason.WrongHost;
            }
            if (senderPlayerId == 0u ||
                entry.Assignment.SimulationOwnerPlayerId != senderPlayerId)
            {
                return EnemySnapshotRejectionReason.WrongOwner;
            }
            if (snapshot.AssignmentEpoch != entry.Assignment.Epoch)
            {
                return EnemySnapshotRejectionReason.WrongEpoch;
            }
            var reason = ValidateMovement(entry, snapshot);
            if (reason != EnemySnapshotRejectionReason.None) return reason;
            AcceptMovement(entry, snapshot);
            return EnemySnapshotRejectionReason.None;
        }

        public EnemyAttackPresentationRejectionReason
            EvidenceCore_TryAcceptClientAttackPresentation(
                uint senderPlayerId,
                EnemyAttackPresentationEdge edge)
        {
            if (!entries.TryGetValue(edge.EnemyEntityId, out Entry entry))
            {
                return EnemyAttackPresentationRejectionReason.UnknownEnemy;
            }
            if (entry.Assignment.Host != EnemySimulationHost.ClientPlayer)
            {
                return EnemyAttackPresentationRejectionReason.WrongHost;
            }
            if (senderPlayerId == 0u ||
                entry.Assignment.SimulationOwnerPlayerId != senderPlayerId)
            {
                return EnemyAttackPresentationRejectionReason.WrongOwner;
            }
            if (edge.AssignmentEpoch != entry.Assignment.Epoch)
            {
                return EnemyAttackPresentationRejectionReason.WrongEpoch;
            }
            if (!EnemySimulationSequence.IsNewer(
                edge.StateSequence,
                entry.LastAcceptedAttackStateSequence))
            {
                return EnemyAttackPresentationRejectionReason.StaleSequence;
            }
            if (!edge.IsFinite || !edge.HasKnownPhase)
            {
                return EnemyAttackPresentationRejectionReason.InvalidValue;
            }
            if (entry.LastAcceptedAttackStateSequence != 0u &&
                edge.StateStartNetworkTime <
                    entry.LastAttackPresentation.StateStartNetworkTime)
            {
                return EnemyAttackPresentationRejectionReason.StaleTimestamp;
            }

            entry.LastAcceptedAttackStateSequence = edge.StateSequence;
            entry.LastAttackPresentation = edge;
            entry.HasAttackPresentation = true;
            if (edge.Checkpoint.Movement.EnemyEntityId == edge.EnemyEntityId &&
                edge.Checkpoint.Movement.AssignmentEpoch == edge.AssignmentEpoch && edge.Checkpoint.Movement.IsFinite)
                RecordCheckpoint(edge.Checkpoint);
            return EnemyAttackPresentationRejectionReason.None;
        }

        private void EvidenceCore_RecordServerSnapshot(EnemySimulationSnapshot snapshot)
        {
            Entry entry = RequireEntry(snapshot.EnemyEntityId);
            if (entry.Assignment.Host != EnemySimulationHost.ServerFallback &&
                entry.Assignment.Host != EnemySimulationHost.ServerAuthoritative)
            {
                throw new InvalidOperationException(
                    $"Enemy {snapshot.EnemyEntityId} is not simulated by the server.");
            }
            var reason = ValidateMovement(entry, snapshot);
            if (reason != EnemySnapshotRejectionReason.None)
            {
                throw new ArgumentException($"Invalid server simulation snapshot: {reason}; enemy={snapshot.EnemyEntityId}, " +
                    $"epoch={snapshot.AssignmentEpoch}/{entry.Assignment.Epoch}, sequence={snapshot.Sequence}/{entry.LastAcceptedSequence}, " +
                    $"time={snapshot.SampleNetworkTime:R}/{entry.LastAcceptedMovementTime:R}, checkpointTime={entry.LastSnapshot.SampleNetworkTime:R}.", nameof(snapshot));
            }
            AcceptMovement(entry, snapshot);
        }

        private static EnemySnapshotRejectionReason ValidateMovement(Entry entry, EnemySimulationSnapshot snapshot)
        {
            if (snapshot.AssignmentEpoch != entry.Assignment.Epoch) return EnemySnapshotRejectionReason.WrongEpoch;
            if (!EnemySimulationSequence.IsNewer(snapshot.Sequence, entry.LastAcceptedSequence)) return EnemySnapshotRejectionReason.StaleSequence;
            if (!snapshot.IsFinite) return EnemySnapshotRejectionReason.InvalidValue;
            if (entry.LastAcceptedSequence != 0 && snapshot.SampleNetworkTime <= entry.LastAcceptedMovementTime)
                return EnemySnapshotRejectionReason.StaleTimestamp;
            return EnemySnapshotRejectionReason.None;
        }

        private static void AcceptMovement(Entry entry, EnemySimulationSnapshot snapshot)
        {
            entry.LastAcceptedSequence = snapshot.Sequence;
            entry.LastAcceptedMovementTime = snapshot.SampleNetworkTime;
            if (!entry.HasSnapshot || snapshot.SampleNetworkTime > entry.LastSnapshot.SampleNetworkTime ||
                (snapshot.SampleNetworkTime == entry.LastSnapshot.SampleNetworkTime && !entry.LastSnapshotIsCheckpoint))
            {
                entry.LastSnapshot = snapshot;
                entry.HasSnapshot = true;
                entry.LastSnapshotIsCheckpoint = false;
            }
        }

        private void EvidenceCore_RecordServerAttackPresentation(
            EnemyAttackPresentationEdge edge)
        {
            Entry entry = RequireEntry(edge.EnemyEntityId);
            if (entry.Assignment.Host != EnemySimulationHost.ServerFallback &&
                entry.Assignment.Host != EnemySimulationHost.ServerAuthoritative)
            {
                throw new InvalidOperationException(
                    $"Enemy {edge.EnemyEntityId} is not simulated by the server.");
            }
            if (edge.AssignmentEpoch != entry.Assignment.Epoch ||
                !EnemySimulationSequence.IsNewer(
                    edge.StateSequence,
                    entry.LastAcceptedAttackStateSequence) ||
                !edge.IsFinite || !edge.HasKnownPhase ||
                (entry.LastAcceptedAttackStateSequence != 0u &&
                 edge.StateStartNetworkTime <
                    entry.LastAttackPresentation.StateStartNetworkTime))
            {
                throw new ArgumentException(
                    "Invalid server Enemy attack presentation edge.",
                    nameof(edge));
            }

            entry.LastAcceptedAttackStateSequence = edge.StateSequence;
            entry.LastAttackPresentation = edge;
            entry.HasAttackPresentation = true;
            if (edge.Checkpoint.Movement.EnemyEntityId == edge.EnemyEntityId)
                RecordCheckpoint(edge.Checkpoint);
        }

        public void GetEnemiesOwnedBy(uint playerId, List<uint> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            foreach (KeyValuePair<uint, Entry> pair in entries)
            {
                if (pair.Value.Assignment.Host == EnemySimulationHost.ClientPlayer &&
                    pair.Value.Assignment.SimulationOwnerPlayerId == playerId)
                {
                    results.Add(pair.Key);
                }
            }
        }

        public void GetEnemiesDependingOnPlayer(uint playerId, List<uint> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            foreach (KeyValuePair<uint, Entry> pair in entries)
            {
                EnemySimulationAssignment assignment = pair.Value.Assignment;
                bool clientOwnerDisconnected =
                    assignment.Host == EnemySimulationHost.ClientPlayer &&
                    assignment.SimulationOwnerPlayerId == playerId;
                bool fallbackTargetDisconnected = assignment.AggroTargetPlayerId == playerId;
                if (clientOwnerDisconnected || fallbackTargetDisconnected)
                {
                    results.Add(pair.Key);
                }
            }
        }

        private Entry RequireEntry(uint enemyEntityId)
        {
            if (!entries.TryGetValue(enemyEntityId, out Entry entry))
            {
                throw new KeyNotFoundException(
                    $"Enemy {enemyEntityId} is not registered for simulation.");
            }

            return entry;
        }

        private EnemySimulationAssignment EvidenceCore_RenewAssignment(uint enemyEntityId)
        {
            var entry = RequireEntry(enemyEntityId);
            var current = entry.Assignment;
            return SetAssignment(entry, current.Host, current.SimulationOwnerPlayerId, current.AggroTargetPlayerId, true);
        }

        private static EnemySimulationAssignment SetAssignment(
            Entry entry,
            EnemySimulationHost host,
            uint ownerPlayerId,
            uint targetPlayerId, bool forceNewEpoch = false)
        {
            bool sameTarget = entry.Assignment.AggroTargetPlayerId == targetPlayerId;
            if (entry.Target.Revision == 0 || entry.Target.AggroPlayerId != targetPlayerId)
                SetTarget(entry, new EnemyTargetState { AggroPlayerId = targetPlayerId });
            if (!forceNewEpoch && entry.Assignment.Host == host && entry.Assignment.SimulationOwnerPlayerId == ownerPlayerId &&
                sameTarget && entry.Assignment.Epoch != 0)
                return entry.Assignment;
            var previousAssignment = entry.Assignment;
            uint epoch = unchecked(entry.Assignment.Epoch + 1u);
            if (epoch == 0u)
            {
                epoch = 1u;
            }

            entry.Assignment = new EnemySimulationAssignment
            {
                EnemyEntityId = entry.Assignment.EnemyEntityId,
                Host = host,
                SimulationOwnerPlayerId = ownerPlayerId,
                AggroTargetPlayerId = targetPlayerId,
                Epoch = epoch
            };
            entry.LastAcceptedSequence = 0u;
            entry.LastAcceptedMovementTime = 0d;
            entry.LastAcceptedAttackStateSequence = 0u;
            entry.HasAttackPresentation = false;
            if (MonsterSupergroup.GAS.CombatEvidence.Enabled) MonsterSupergroup.GAS.CombatEvidence.Write(new MonsterSupergroup.GAS.DiagnosticRecord {
                role = "Server", stage = "authority.assignment", outcome = "Applied", reason = forceNewEpoch ? "EpochRenewed" : "OwnerOrTargetChanged",
                source = ownerPlayerId, target = entry.Assignment.EnemyEntityId, assignmentEpoch = epoch,
                before = previousAssignment, after = entry.Assignment, critical = true });
            return entry.Assignment;
        }
    }
}
