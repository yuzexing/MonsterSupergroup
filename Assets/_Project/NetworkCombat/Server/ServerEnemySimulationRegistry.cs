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
    public sealed class ServerEnemySimulationRegistry
    {
        private sealed class Entry
        {
            public EnemySimulationAssignment Assignment;
            public EnemySimulationSnapshot LastSnapshot;
            public bool HasSnapshot;
            public uint LastAcceptedSequence;
            public EnemyAttackPresentationEdge LastAttackPresentation;
            public bool HasAttackPresentation;
            public uint LastAcceptedAttackStateSequence;
        }

        private readonly Dictionary<uint, Entry> entries =
            new Dictionary<uint, Entry>();

        public int Count => entries.Count;

        public void RegisterEnemy(
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

        public void UnregisterEnemy(uint enemyEntityId)
        {
            entries.Remove(enemyEntityId);
        }

        public EnemySimulationAssignment AssignClientOwner(
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

        public EnemySimulationAssignment AssignServerFallback(
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

        public EnemySimulationAssignment AssignServerAuthoritative(
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

        public EnemySimulationAssignment Freeze(uint enemyEntityId)
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

        public bool TryGetLatestSnapshot(
            uint enemyEntityId,
            out EnemySimulationSnapshot snapshot)
        {
            if (entries.TryGetValue(enemyEntityId, out Entry entry) && entry.HasSnapshot)
            {
                snapshot = entry.LastSnapshot;
                return true;
            }

            snapshot = default;
            return false;
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

        public EnemySnapshotRejectionReason TryAcceptClientSnapshot(
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
            if (!EnemySimulationSequence.IsNewer(
                snapshot.Sequence,
                entry.LastAcceptedSequence))
            {
                return EnemySnapshotRejectionReason.StaleSequence;
            }
            if (!snapshot.IsFinite)
            {
                return EnemySnapshotRejectionReason.InvalidValue;
            }
            if (entry.LastAcceptedSequence != 0u &&
                snapshot.SampleNetworkTime <= entry.LastSnapshot.SampleNetworkTime)
            {
                return EnemySnapshotRejectionReason.StaleTimestamp;
            }

            entry.LastAcceptedSequence = snapshot.Sequence;
            entry.LastSnapshot = snapshot;
            entry.HasSnapshot = true;
            return EnemySnapshotRejectionReason.None;
        }

        public EnemyAttackPresentationRejectionReason
            TryAcceptClientAttackPresentation(
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
            return EnemyAttackPresentationRejectionReason.None;
        }

        public void RecordServerSnapshot(EnemySimulationSnapshot snapshot)
        {
            Entry entry = RequireEntry(snapshot.EnemyEntityId);
            if (entry.Assignment.Host != EnemySimulationHost.ServerFallback &&
                entry.Assignment.Host != EnemySimulationHost.ServerAuthoritative)
            {
                throw new InvalidOperationException(
                    $"Enemy {snapshot.EnemyEntityId} is not simulated by the server.");
            }
            if (snapshot.AssignmentEpoch != entry.Assignment.Epoch ||
                !EnemySimulationSequence.IsNewer(
                    snapshot.Sequence,
                    entry.LastAcceptedSequence) ||
                !snapshot.IsFinite ||
                (entry.LastAcceptedSequence != 0u &&
                 snapshot.SampleNetworkTime <= entry.LastSnapshot.SampleNetworkTime))
            {
                throw new ArgumentException("Invalid server simulation snapshot.", nameof(snapshot));
            }

            entry.LastAcceptedSequence = snapshot.Sequence;
            entry.LastSnapshot = snapshot;
            entry.HasSnapshot = true;
        }

        public void RecordServerAttackPresentation(
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
                bool fallbackTargetDisconnected =
                    (assignment.Host == EnemySimulationHost.ServerFallback ||
                     assignment.Host == EnemySimulationHost.ServerAuthoritative) &&
                    assignment.AggroTargetPlayerId == playerId;
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

        private static EnemySimulationAssignment SetAssignment(
            Entry entry,
            EnemySimulationHost host,
            uint ownerPlayerId,
            uint targetPlayerId)
        {
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
            entry.LastAcceptedAttackStateSequence = 0u;
            entry.HasAttackPresentation = false;
            return entry.Assignment;
        }
    }
}
