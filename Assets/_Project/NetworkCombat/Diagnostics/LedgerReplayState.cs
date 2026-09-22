using System;
using System.Collections.Generic;
using System.Linq;

namespace MonsterSupergroup.NetworkCombat
{
    [Serializable] public sealed class LedgerReplayState
    {
        public int maximumDamage;
        public LedgerReplayEntity[] entities;
        public KeyValuePair<uint, uint>[] sourceOwners;
    }
    [Serializable] public struct LedgerReplayEntity
    {
        public CanonicalEntityState state;
        public bool absolute, ultimate, trap, selection;
    }
    public sealed partial class CombatLedger
    {
        public LedgerReplayState CaptureReplayState() => new LedgerReplayState {
            maximumDamage = MaximumDamagePerResult, sourceOwners = sourceOwners.OrderBy(p => p.Key).ToArray(),
            entities = entities.Values.OrderBy(e => e.EntityId).Select(e => new LedgerReplayEntity {
                state = e.ToState(), absolute = e.AbsoluteInvulnerable, ultimate = e.UltimateInvulnerable,
                trap = e.TrapInvulnerable, selection = e.UpgradeSelectionActive }).ToArray() };
        public static CombatLedger RestoreReplayState(LedgerReplayState snapshot)
        {
            var ledger = new CombatLedger(snapshot.maximumDamage);
            foreach (var pair in snapshot.sourceOwners) ledger.sourceOwners.Add(pair.Key, pair.Value);
            foreach (var item in snapshot.entities)
            {
                var s = item.state;
                ledger.entities.Add(s.EntityId, new EntityEntry { EntityId = s.EntityId, OwnerPlayerId = s.OwnerPlayerId,
                    Kind = (CombatEntityKind)s.Kind, Authority = (CombatEntityAuthority)s.Authority, Health = s.Health,
                    MaxHealth = s.MaxHealth, Alive = s.Alive, Version = s.StateVersion, KillerPlayerId = s.KillerPlayerId,
                    AbsoluteInvulnerable = item.absolute, UltimateInvulnerable = item.ultimate,
                    TrapInvulnerable = item.trap, UpgradeSelectionActive = item.selection });
            }
            return ledger;
        }
    }
    [Serializable] public sealed class ProcessedReplayState { public int capacity; public double retention; public ProcessedReplayEntry[] entries; }
    [Serializable] public struct ProcessedReplayEntry { public ulong id; public double expiresAt; }
    public sealed partial class ProcessedEventCache
    {
        public ProcessedReplayState CaptureReplayState() => new ProcessedReplayState { capacity = capacity,
            retention = retentionSeconds, entries = expiryOrder.Select(e => new ProcessedReplayEntry { id = e.EventId, expiresAt = e.ExpiresAt }).ToArray() };
        public static ProcessedEventCache RestoreReplayState(ProcessedReplayState state)
        {
            var cache = new ProcessedEventCache(state.capacity, state.retention);
            foreach (var e in state.entries) { cache.ids.Add(e.id); cache.expiryOrder.Enqueue(new Entry(e.id, e.expiresAt)); }
            return cache;
        }
    }
    [Serializable] public struct ClientIdentityReplayState { public uint player; public ushort slot, epoch; }
    public sealed partial class ClientEventIdentityRegistry
    {
        public ClientIdentityReplayState[] CaptureReplayState() => identities.OrderBy(p => p.Key).Select(p =>
            new ClientIdentityReplayState { player = p.Key, slot = p.Value.SourceSlot, epoch = p.Value.ConnectionEpoch }).ToArray();
        public static ClientEventIdentityRegistry RestoreReplayState(ClientIdentityReplayState[] state)
        { var result = new ClientEventIdentityRegistry(); foreach (var item in state) result.Register(item.player, item.slot, item.epoch); return result; }
    }
    [Serializable] public sealed class BatchTrackerReplayState { public uint maximumForwardJump; public KeyValuePair<uint, uint>[] sequences; }
    public sealed partial class ClientBatchSequenceTracker
    {
        public BatchTrackerReplayState CaptureReplayState() => new BatchTrackerReplayState {
            maximumForwardJump = maximumForwardJump, sequences = highestSequences.OrderBy(p => p.Key).ToArray() };
        public static ClientBatchSequenceTracker RestoreReplayState(BatchTrackerReplayState state)
        { var result = new ClientBatchSequenceTracker(state.maximumForwardJump); foreach (var item in state.sequences) result.highestSequences.Add(item.Key, item.Value); return result; }
    }
}
