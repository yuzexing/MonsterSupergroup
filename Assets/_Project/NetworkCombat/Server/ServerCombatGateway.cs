using System;
using System.Collections.Generic;
using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed class CombatGatewayMetrics
    {
        private readonly Dictionary<CombatRejectionReason, long> rejections =
            new Dictionary<CombatRejectionReason, long>();

        public long AcceptedCombatResults { get; internal set; }
        public long AcceptedStatusMutations { get; internal set; }
        public long AcceptedPlayerReports { get; internal set; }
        public long ConfirmedKills { get; internal set; }
        public long ReceivedBatches { get; internal set; }
        public long RejectedBatches { get; internal set; }
        public long ReceivedCombatResults { get; internal set; }
        public long ReceivedStatusMutations { get; internal set; }
        public long ReceivedPlayerReports { get; internal set; }
        public long EstimatedReceivedPayloadBytes { get; internal set; }

        public long GetRejected(CombatRejectionReason reason)
        {
            return rejections.TryGetValue(reason, out long count) ? count : 0;
        }

        internal void Reject(CombatRejectionReason reason)
        {
            rejections.TryGetValue(reason, out long count);
            rejections[reason] = count + 1;
        }
    }

    /// <summary>
    /// Transport-neutral server boundary. It accepts resolved client outcomes and
    /// merges only shared canonical facts; it never reruns player GAS/build logic.
    /// </summary>
    public sealed class ServerCombatGateway
    {
        private readonly ICombatEventIdSource serverEventIds;
        private readonly CombatTraceRecorder trace;
        private uint serverSequence;

        public ServerCombatGateway(
            CombatLedger ledger = null,
            ICombatEventIdSource serverEventIds = null,
            CombatTraceRecorder trace = null)
        {
            Ledger = ledger ?? new CombatLedger();
            Statuses = new ServerStatusRegistry(Ledger);
            this.serverEventIds = serverEventIds ??
                new SequentialCombatEventIdSource(ushort.MaxValue, 1);
            this.trace = trace;
        }

        public CombatLedger Ledger { get; }
        public ServerStatusRegistry Statuses { get; }
        public CombatGatewayMetrics Metrics { get; } = new CombatGatewayMetrics();
        public ProcessedEventCache ProcessedEvents { get; } = new ProcessedEventCache();
        public ClientEventIdentityRegistry ClientIdentities { get; } =
            new ClientEventIdentityRegistry();
        public ClientBatchSequenceTracker BatchSequences { get; } =
            new ClientBatchSequenceTracker();

        public int MaximumResultsPerBatch { get; set; } = 512;
        public int MaximumStatusMutationsPerBatch { get; set; } = 256;
        public int MaximumPlayerReportsPerBatch { get; set; } = 16;

        public event Action<ConfirmedKill> ConfirmedKillProduced;
        public event Action<ServerStatusTick> ServerStatusTickProduced;

        public void RegisterClientIdentity(
            uint playerId,
            ushort sourceSlot,
            ushort connectionEpoch)
        {
            ClientIdentities.Register(playerId, sourceSlot, connectionEpoch);
        }

        public void UnregisterClientIdentity(uint playerId)
        {
            ClientIdentities.Unregister(playerId);
            BatchSequences.Remove(playerId);
        }

        public CanonicalWorldBatch ProcessBatch(
            uint senderPlayerId,
            CombatSubmissionBatch batch,
            double serverTime)
        {
            ValidateServerTime(serverTime);
            var entities = new Dictionary<uint, CanonicalEntityState>();
            var statuses = new List<CanonicalStatusState>();
            var kills = new List<ConfirmedKill>();

            CombatResult[] results = batch.Results ?? Array.Empty<CombatResult>();
            StatusMutation[] mutations = batch.StatusMutations ?? Array.Empty<StatusMutation>();
            PlayerHealthReport[] playerReports =
                batch.PlayerHealthReports ?? Array.Empty<PlayerHealthReport>();
            Metrics.ReceivedBatches++;
            Metrics.ReceivedCombatResults += results.Length;
            Metrics.ReceivedStatusMutations += mutations.Length;
            Metrics.ReceivedPlayerReports += playerReports.Length;
            Metrics.EstimatedReceivedPayloadBytes +=
                CombatBandwidthEstimator.EstimatePayloadBytes(batch);
            if (!BatchSequences.Accept(senderPlayerId, batch.BatchSequence) ||
                results.Length > MaximumResultsPerBatch ||
                mutations.Length > MaximumStatusMutationsPerBatch ||
                playerReports.Length > MaximumPlayerReportsPerBatch)
            {
                Metrics.RejectedBatches++;
                Metrics.Reject(CombatRejectionReason.InvalidSequence);
                return CreateBatch(entities.Values, statuses, kills);
            }

            for (int i = 0; i < results.Length; i++)
            {
                if (!ClientIdentities.Validate(
                        senderPlayerId,
                        results[i].EventId,
                        results[i].Sequence))
                {
                    Metrics.Reject(CombatRejectionReason.InvalidSequence);
                    continue;
                }

                if (ProcessedEvents.IsProcessed(results[i].EventId, serverTime))
                {
                    Metrics.Reject(CombatRejectionReason.DuplicateEvent);
                    continue;
                }

                CombatApplyResult applied = Ledger.Apply(senderPlayerId, results[i]);
                if (!applied.Accepted)
                {
                    Metrics.Reject(applied.Rejection);
                    continue;
                }

                Metrics.AcceptedCombatResults++;
                ProcessedEvents.MarkProcessed(results[i].EventId, serverTime);
                entities[applied.State.EntityId] = applied.State;
                RecordDamage(results[i]);
                if (applied.IsConfirmedKill)
                {
                    AddConfirmedKill(applied.Kill, kills);
                    statuses.AddRange(Statuses.RemoveTarget(applied.State.EntityId));
                }
            }

            for (int i = 0; i < mutations.Length; i++)
            {
                if (!ClientIdentities.Validate(
                        senderPlayerId,
                        mutations[i].EventId,
                        mutations[i].Sequence))
                {
                    Metrics.Reject(CombatRejectionReason.InvalidSequence);
                    continue;
                }

                if (ProcessedEvents.IsProcessed(mutations[i].EventId, serverTime))
                {
                    Metrics.Reject(CombatRejectionReason.DuplicateEvent);
                    continue;
                }

                StatusMutationResult applied = Statuses.Apply(
                    senderPlayerId,
                    mutations[i],
                    serverTime);
                if (!applied.Accepted)
                {
                    Metrics.Reject(applied.Rejection);
                    continue;
                }

                Metrics.AcceptedStatusMutations++;
                ProcessedEvents.MarkProcessed(mutations[i].EventId, serverTime);
                statuses.Add(applied.State);
                RecordStatus(mutations[i], applied.State);
            }

            for (int i = 0; i < playerReports.Length; i++)
            {
                if (!ClientIdentities.Validate(
                        senderPlayerId,
                        playerReports[i].EventId,
                        playerReports[i].Sequence))
                {
                    Metrics.Reject(CombatRejectionReason.InvalidSequence);
                    continue;
                }

                if (ProcessedEvents.IsProcessed(playerReports[i].EventId, serverTime))
                {
                    Metrics.Reject(CombatRejectionReason.DuplicateEvent);
                    continue;
                }

                CombatApplyResult applied = Ledger.ApplyOwnerFinalReport(
                    senderPlayerId,
                    playerReports[i]);
                if (!applied.Accepted)
                {
                    Metrics.Reject(applied.Rejection);
                    continue;
                }

                Metrics.AcceptedPlayerReports++;
                ProcessedEvents.MarkProcessed(playerReports[i].EventId, serverTime);
                entities[applied.State.EntityId] = applied.State;
                if (applied.IsConfirmedKill)
                {
                    AddConfirmedKill(applied.Kill, kills);
                }
            }

            return CreateBatch(entities.Values, statuses, kills);
        }

        public CanonicalWorldBatch Advance(double serverTime)
        {
            var entities = new Dictionary<uint, CanonicalEntityState>();
            var kills = new List<ConfirmedKill>();
            StatusAdvanceResult statusAdvance = Statuses.Advance(serverTime);
            for (int i = 0; i < statusAdvance.Ticks.Count; i++)
            {
                ServerStatusTick tick = statusAdvance.Ticks[i];
                ServerStatusTickProduced?.Invoke(tick);
                if (tick.Instance.TickDamage <= 0)
                {
                    continue;
                }

                CombatEventId eventId = serverEventIds.Next();
                CombatApplyResult applied = Ledger.ApplyServerStatusDamage(
                    tick.Instance.TargetEntityId,
                    tick.Instance.TickDamage,
                    eventId.Value,
                    tick.Instance.SourcePlayerId);
                if (!applied.Accepted)
                {
                    Metrics.Reject(applied.Rejection);
                    continue;
                }

                entities[applied.State.EntityId] = applied.State;
                if (applied.IsConfirmedKill)
                {
                    AddConfirmedKill(applied.Kill, kills);
                    statusAdvance.Changes.AddRange(Statuses.RemoveTarget(applied.State.EntityId));
                }
            }

            return CreateBatch(entities.Values, statusAdvance.Changes, kills);
        }

        public CanonicalWorldBatch HandleSourceDisconnected(
            uint sourcePlayerId,
            double serverTime)
        {
            IReadOnlyList<CanonicalStatusState> changes =
                Statuses.HandleSourceDisconnected(sourcePlayerId, serverTime);
            return CreateBatch(
                Array.Empty<CanonicalEntityState>(),
                changes,
                Array.Empty<ConfirmedKill>());
        }

        /// <summary>Returns all current canonical facts for a newly ready client.</summary>
        public CanonicalWorldBatch CreateSnapshot()
        {
            return CreateBatch(
                Ledger.GetAllStates(),
                Statuses.GetAllStates(),
                Array.Empty<ConfirmedKill>());
        }

        /// <summary>Wraps one newly registered entity in the normal sequence stream.</summary>
        public CanonicalWorldBatch CreateEntityUpdate(CanonicalEntityState state)
        {
            return CreateBatch(
                new[] { state },
                Array.Empty<CanonicalStatusState>(),
                Array.Empty<ConfirmedKill>());
        }

        private void AddConfirmedKill(ConfirmedKill kill, ICollection<ConfirmedKill> destination)
        {
            destination.Add(kill);
            Metrics.ConfirmedKills++;
            trace?.RecordConfirmedKill(
                new CombatEventId(kill.CauseEventId),
                kill.KillerPlayerId,
                kill.TargetEntityId,
                kill.TargetStateVersion);
            ConfirmedKillProduced?.Invoke(kill);
        }

        private void RecordDamage(CombatResult result)
        {
            if (trace == null)
            {
                return;
            }

            var context = new CombatContext(
                new CombatEventId(result.EventId),
                new CombatEventId(result.RootEventId),
                new CombatEventId(result.ParentEventId),
                result.Sequence,
                result.ChainDepth,
                result.SourcePlayerId,
                result.SourceEntityId,
                result.TargetEntityId,
                result.AbilityId,
                result.BuildId,
                (CombatTags)result.DamageTags,
                result.TargetStateVersion);
            trace.RecordResolvedDamage(context, result.Damage);
        }

        private void RecordStatus(StatusMutation mutation, CanonicalStatusState state)
        {
            if (trace == null || state.Removed)
            {
                return;
            }

            StatusInstance instance = state.ToStatusInstance();
            var change = new StatusChange(
                StatusChangeKind.Updated,
                StatusStateOrigin.Canonical,
                instance);
            trace.RecordStatus(change, new CombatEventId(mutation.EventId));
        }

        private static void ValidateServerTime(double serverTime)
        {
            if (double.IsNaN(serverTime) || double.IsInfinity(serverTime))
            {
                throw new ArgumentOutOfRangeException(nameof(serverTime));
            }
        }

        private CanonicalWorldBatch CreateBatch(
            IEnumerable<CanonicalEntityState> entities,
            IEnumerable<CanonicalStatusState> statuses,
            IEnumerable<ConfirmedKill> kills)
        {
            serverSequence = unchecked(serverSequence + 1u);
            if (serverSequence == 0u)
            {
                serverSequence = 1u;
            }

            return new CanonicalWorldBatch
            {
                ServerSequence = serverSequence,
                Entities = ToArray(entities),
                Statuses = ToArray(statuses),
                ConfirmedKills = ToArray(kills)
            };
        }

        private static T[] ToArray<T>(IEnumerable<T> source)
        {
            if (source is ICollection<T> collection)
            {
                var result = new T[collection.Count];
                collection.CopyTo(result, 0);
                return result;
            }

            return new List<T>(source).ToArray();
        }
    }
}
