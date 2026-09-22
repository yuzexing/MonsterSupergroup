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
        public long ReceivedEnemyDeathReports { get; internal set; }
        public long ConfirmedEnemyDeathReports { get; internal set; }

        public long GetRejected(CombatRejectionReason reason)
        {
            return rejections.TryGetValue(reason, out long count) ? count : 0;
        }

        internal void Reject(CombatRejectionReason reason, string diagnosticReason = null)
        {
            if (CombatEvidence.Enabled) GatewayEvidenceDecision.Reject(diagnosticReason ?? reason.ToString());
            rejections.TryGetValue(reason, out long count);
            rejections[reason] = count + 1;
        }
    }

    /// <summary>
    /// Transport-neutral server boundary. It accepts resolved client outcomes and
    /// merges only shared canonical facts; it never reruns player GAS/build logic.
    /// </summary>
    public sealed partial class ServerCombatGateway
    {
        private readonly ICombatEventIdSource serverEventIds;
        private readonly CombatTraceRecorder trace;
        private uint serverSequence;
        private readonly Dictionary<uint, ConfirmedKill> enemyDeaths = new Dictionary<uint, ConfirmedKill>();
        private readonly HashSet<uint> retiredEnemies = new HashSet<uint>();
        private readonly HashSet<uint> disconnectedPlayers = new HashSet<uint>();
        private uint evidenceRound;
        public uint Round
        {
            get => evidenceRound;
            set
            {
                if (evidenceRound == value) return;
                using var evidence = CombatEvidence.Enabled ? CombatEvidence.Begin(this, "gateway", "SetRound", new object[] { value }, o => ((ServerCombatGateway)o).CaptureReplayState()) : default;
                evidenceRound = value; evidence.Complete();
            }
        }

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
            BindEvidence();
        }

        public CombatLedger Ledger { get; private set; }
        public ServerStatusRegistry Statuses { get; private set; }
        public ServerAttackRegistry Attacks { get; private set; } = new ServerAttackRegistry();
        public ServerStatusDamageAdmissions StatusDamageAdmissions { get; private set; } = new ServerStatusDamageAdmissions();
        public CombatGatewayMetrics Metrics { get; private set; } = new CombatGatewayMetrics();
        public ProcessedEventCache ProcessedEvents { get; private set; } = new ProcessedEventCache();
        public ClientEventIdentityRegistry ClientIdentities { get; private set; } =
            new ClientEventIdentityRegistry();
        public ClientBatchSequenceTracker BatchSequences { get; private set; } =
            new ClientBatchSequenceTracker();

        public bool CombatStopped { get; private set; }
        private void EvidenceCore_StopCombat() => CombatStopped = true;
        private void EvidenceCore_ResetForNextRun()
        {
            Statuses.Clear();
            Ledger = new CombatLedger(); Statuses = new ServerStatusRegistry(Ledger);
            Attacks = new ServerAttackRegistry(); StatusDamageAdmissions = new ServerStatusDamageAdmissions();
            Metrics = new CombatGatewayMetrics(); ProcessedEvents = new ProcessedEventCache();
            ClientIdentities = new ClientEventIdentityRegistry(); BatchSequences = new ClientBatchSequenceTracker();
            enemyDeaths.Clear(); retiredEnemies.Clear(); disconnectedPlayers.Clear();
            BindEvidence();
            // Keep event IDs monotonic across rounds, along with the event subscriptions.
            CombatStopped = false;
        }

        public int MaximumResultsPerBatch { get; set; } = 512;
        public int MaximumStatusMutationsPerBatch { get; set; } = 256;
        public int MaximumPlayerReportsPerBatch { get; set; } = 16;

        public event Action<ConfirmedKill> ConfirmedKillProduced;
        public event Action<ServerStatusTick> ServerStatusTickProduced;
        public event Action<CombatResult, CombatApplyResult, double> CombatResultAccepted;
        public Func<uint, PlayerHealthReport, bool> ValidatePickupReceipt { get; set; }
        public event Action<PlayerHealthReport> PlayerHealthReportAccepted;
        public event Action<uint, PlayerHealthReport, CombatRejectionReason> PlayerHealthReportRejected;

        private void EvidenceCore_RegisterClientIdentity(
            uint playerId,
            ushort sourceSlot,
            ushort connectionEpoch)
        {
            ClientIdentities.Register(playerId, sourceSlot, connectionEpoch);
            disconnectedPlayers.Remove(playerId);
        }

        private void EvidenceCore_UnregisterClientIdentity(uint playerId)
        {
            disconnectedPlayers.Add(playerId);
            Attacks.UnregisterPlayer(playerId);
            StatusDamageAdmissions.RemovePlayer(playerId);
            ClientIdentities.Unregister(playerId);
            BatchSequences.Remove(playerId);
        }

        public CanonicalWorldBatch ProcessBatch(
            uint senderPlayerId,
            CombatSubmissionBatch batch,
            double serverTime)
            => ProcessBatch(senderPlayerId, batch, serverTime, out _);

        private CanonicalWorldBatch EvidenceCore_ProcessBatch(uint senderPlayerId, CombatSubmissionBatch batch,
            double serverTime, out EnemyDeathReceipt[] deathReceipts)
        {
            deathReceipts = Array.Empty<EnemyDeathReceipt>();
            if (CombatStopped) { GatewayEvidenceDecision.Reject("CombatStopped"); return default; }
            if (disconnectedPlayers.Contains(senderPlayerId))
            { Metrics.Reject(CombatRejectionReason.InvalidSender); return default; }
            ValidateServerTime(serverTime);
            var entities = new Dictionary<uint, CanonicalEntityState>();
            var statuses = new List<CanonicalStatusState>();
            var kills = new List<ConfirmedKill>();

            var hits = new List<EnemyHitPresentation>();

            CombatResult[] results = batch.Results ?? Array.Empty<CombatResult>();
            StatusMutation[] mutations = batch.StatusMutations ?? Array.Empty<StatusMutation>();
            PlayerHealthReport[] playerReports =
                batch.PlayerHealthReports ?? Array.Empty<PlayerHealthReport>();
            EnemyDeathReport[] deaths = batch.EnemyDeathReports ?? Array.Empty<EnemyDeathReport>();
            Metrics.ReceivedBatches++;
            Metrics.ReceivedCombatResults += results.Length;
            Metrics.ReceivedStatusMutations += mutations.Length;
            Metrics.ReceivedPlayerReports += playerReports.Length;
            Metrics.ReceivedEnemyDeathReports += deaths.Length;
            Metrics.EstimatedReceivedPayloadBytes +=
                CombatBandwidthEstimator.EstimatePayloadBytes(batch);
            string batchReason = batch.Round != Round ? "WrongRound" : !BatchSequences.Accept(senderPlayerId, batch.BatchSequence) ? "StaleOrInvalidBatch" :
                results.Length > MaximumResultsPerBatch || mutations.Length > MaximumStatusMutationsPerBatch ||
                playerReports.Length > MaximumPlayerReportsPerBatch || deaths.Length > MaximumResultsPerBatch ? "BatchCapacityExceeded" : null;
            if (batchReason != null)
            {
                Metrics.RejectedBatches++;
                Metrics.Reject(CombatRejectionReason.InvalidSequence, batchReason);
                return CreateBatch(entities.Values, statuses, kills);
            }

            for (int i = 0; i < results.Length; i++)
            {
                if (Attacks.RequiresAdmission(senderPlayerId) && ServerStatusDamageAdmissions.IsPeriodic(results[i]))
                    continue;
                ApplyResult(results[i]);
            }

            void ApplyResult(CombatResult result)
            {
                GatewayEvidenceDecision.Select(result);
                if (!ClientIdentities.Validate(
                        senderPlayerId,
                        result.EventId,
                        result.Sequence))
                {
                    Metrics.Reject(CombatRejectionReason.InvalidSequence);
                    return;
                }

                if (ProcessedEvents.IsProcessed(result.EventId, serverTime))
                {
                    Metrics.Reject(CombatRejectionReason.DuplicateEvent);
                    return;
                }

                bool periodic = Attacks.RequiresAdmission(senderPlayerId) && ServerStatusDamageAdmissions.IsPeriodic(result);
                bool clientFinalEnemy = IsEnemy(result.TargetEntityId);
                CombatRejectionReason admission = clientFinalEnemy
                    ? (ServerStatusDamageAdmissions.IsPeriodic(result) ? CombatRejectionReason.None : ServerAttackRegistry.ValidateOutcomeIdentity(result)) : periodic
                    ? StatusDamageAdmissions.Validate(result, serverTime) : Attacks.Validate(result);
                if (admission != CombatRejectionReason.None)
                {
                    Metrics.Reject(admission);
                    return;
                }
                CombatApplyResult applied = Ledger.Apply(senderPlayerId, result);
                if (!applied.Accepted)
                {
                    Metrics.Reject(applied.Rejection);
                    if (applied.Rejection == CombatRejectionReason.SourceSelectingUpgrade)
                        ProcessedEvents.MarkProcessed(result.EventId, serverTime);
                    return;
                }

                if (periodic) StatusDamageAdmissions.Commit(result);
                GatewayEvidenceDecision.Accept(applied.State);
                Metrics.AcceptedCombatResults++;
                ProcessedEvents.MarkProcessed(result.EventId, serverTime);
                entities[applied.State.EntityId] = applied.State;
                RecordDamage(result);
                AddEnemyHit(result, applied, hits);
                CombatResultAccepted?.Invoke(result, applied, serverTime);
                if (applied.IsConfirmedKill)
                {
                    AddConfirmedKill(applied.Kill, kills);
                    statuses.AddRange(Statuses.RemoveTarget(applied.State.EntityId));
                }
            }

            for (int i = 0; i < mutations.Length; i++)
            {
                GatewayEvidenceDecision.Select(mutations[i]);
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

                CombatRejectionReason admission = IsEnemy(mutations[i].TargetEntityId)
                    ? CombatRejectionReason.None : Attacks.Validate(mutations[i]);
                if (admission != CombatRejectionReason.None)
                {
                    Metrics.Reject(admission);
                    continue;
                }
                StatusMutationResult applied = Statuses.Apply(
                    senderPlayerId,
                    mutations[i],
                    serverTime);
                if (!applied.Accepted)
                {
                    Metrics.Reject(applied.Rejection);
                    if (applied.Rejection == CombatRejectionReason.SourceSelectingUpgrade)
                        ProcessedEvents.MarkProcessed(mutations[i].EventId, serverTime);
                    continue;
                }

                GatewayEvidenceDecision.Accept(applied.State);
                Metrics.AcceptedStatusMutations++;
                if (Attacks.RequiresAdmission(senderPlayerId))
                    StatusDamageAdmissions.Observe(mutations[i], applied.State, serverTime);
                ProcessedEvents.MarkProcessed(mutations[i].EventId, serverTime);
                statuses.Add(applied.State);
                RecordStatus(mutations[i], applied.State);
            }

            // A source tick can share a batch with its status application metadata.
            // Ordinary hit/kill ordering remains unchanged; only periodic results wait here.
            for (int i = 0; i < results.Length; i++)
                if (Attacks.RequiresAdmission(senderPlayerId) && ServerStatusDamageAdmissions.IsPeriodic(results[i]))
                    ApplyResult(results[i]);

            if (deaths.Length > 0)
            {
                var receipts = new List<EnemyDeathReceipt>(deaths.Length);
                foreach (var report in deaths)
                {
                    GatewayEvidenceDecision.Select(report);
                    var cause = new CombatEventId(report.CauseEventId);
                    if (report.SourcePlayerId != senderPlayerId || senderPlayerId == 0 ||
                        !Ledger.IsSourceOwnedBy(report.SourceEntityId, senderPlayerId))
                    { Metrics.Reject(CombatRejectionReason.SourceNotOwned); continue; }
                    if (!ClientIdentities.Validate(senderPlayerId, report.EventId, report.Sequence) ||
                        !ClientIdentities.Validate(senderPlayerId, report.CauseEventId, cause.Sequence) ||
                        cause.Sequence >= report.Sequence || report.TargetEntityId == 0)
                    { Metrics.Reject(CombatRejectionReason.InvalidSequence); continue; }
                    if (!enemyDeaths.TryGetValue(report.TargetEntityId, out var kill) &&
                        !retiredEnemies.Contains(report.TargetEntityId))
                    {
                        if (ProcessedEvents.IsProcessed(report.EventId, serverTime))
                        { Metrics.Reject(CombatRejectionReason.DuplicateEvent); continue; }
                        var applied = Ledger.ApplyEnemyDeath(senderPlayerId, report);
                        if (!applied.Accepted) { Metrics.Reject(applied.Rejection); continue; }
                        entities[report.TargetEntityId] = applied.State;
                        kill = applied.Kill;
                        if (applied.IsConfirmedKill)
                        {
                            AddConfirmedKill(kill, kills);
                            statuses.AddRange(Statuses.RemoveTarget(report.TargetEntityId));
                        }
                    }
                    ProcessedEvents.MarkProcessed(report.EventId, serverTime);
                    GatewayEvidenceDecision.Accept(new { report.TargetEntityId, kill });
                    Metrics.ConfirmedEnemyDeathReports++;
                    receipts.Add(new EnemyDeathReceipt
                    { ReportEventId = report.EventId, TargetEntityId = report.TargetEntityId, Kill = kill });
                }
                deathReceipts = receipts.ToArray();
            }

            for (int i = 0; i < playerReports.Length; i++)
            {
                GatewayEvidenceDecision.Select(playerReports[i]);
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

                if (playerReports[i].PickupDropId != 0 &&
                    !ValidatePickupForEvidence(senderPlayerId, playerReports[i]))
                {
                    Metrics.Reject(CombatRejectionReason.InvalidSender);
                    PlayerHealthReportRejected?.Invoke(senderPlayerId, playerReports[i], CombatRejectionReason.InvalidSender);
                    continue;
                }
                CombatApplyResult applied = Ledger.ApplyOwnerFinalReport(
                    senderPlayerId,
                    playerReports[i]);
                if (!applied.Accepted)
                {
                    Metrics.Reject(applied.Rejection);
                    PlayerHealthReportRejected?.Invoke(senderPlayerId, playerReports[i], applied.Rejection);
                    if (applied.Rejection == CombatRejectionReason.AbsoluteInvulnerable &&
                        applied.State.EntityId != 0u)
                    {
                        ProcessedEvents.MarkProcessed(playerReports[i].EventId, serverTime);
                        entities[applied.State.EntityId] = applied.State;
                    }
                    continue;
                }

                GatewayEvidenceDecision.Accept(applied.State);
                Metrics.AcceptedPlayerReports++;
                // Synchronous with the ledger update, before another claim or disconnect can run.
                PlayerHealthReportAccepted?.Invoke(playerReports[i]);
                ProcessedEvents.MarkProcessed(playerReports[i].EventId, serverTime);
                entities[applied.State.EntityId] = applied.State;
                if (applied.IsConfirmedKill)
                {
                    AddConfirmedKill(applied.Kill, kills);
                }
            }

            return CreateBatch(entities.Values, statuses, kills, hits);
        }

        private bool ValidatePickupForEvidence(uint sender, PlayerHealthReport report)
        {
            bool accepted = ValidatePickupReceipt != null && ValidatePickupReceipt(sender, report);
            if (CombatEvidence.Enabled) CombatEvidence.Event("ExternalPickupService", "replay.external", "Observed", "PickupReceiptFact",
                report.EventId, sender, report.EntityId, new { sender, report, accepted });
            return accepted;
        }
        private CombatApplyResult EvidenceCore_ProcessGluttonyDevour(uint player, uint source, uint target,
            ulong eventId, double now, out CanonicalWorldBatch batch)
        {
            batch = default;
            if (CombatStopped) return CombatApplyResult.Reject(CombatRejectionReason.RunLoading);
            ValidateServerTime(now);
            if (!ClientIdentities.Validate(player, eventId, new CombatEventId(eventId).Sequence))
                return CombatApplyResult.Reject(CombatRejectionReason.InvalidSequence);
            if (ProcessedEvents.IsProcessed(eventId, now))
                return CombatApplyResult.Reject(CombatRejectionReason.DuplicateEvent);
            var result = Ledger.ApplyGluttonyDevour(player, source, target, eventId);
            if (!result.Accepted) return result;
            ProcessedEvents.MarkProcessed(eventId, now);
            var removed = Statuses.RemoveTarget(target);
            var kills = new List<ConfirmedKill>(1);
            if (result.IsConfirmedKill) AddConfirmedKill(result.Kill, kills);
            batch = CreateBatch(new[] { result.State }, removed, kills);
            return result;
        }

        private CanonicalWorldBatch EvidenceCore_Advance(double serverTime)
        {
            if (CombatStopped) return default;
            StatusDamageAdmissions.Prune(serverTime);
            var entities = new Dictionary<uint, CanonicalEntityState>();
            var kills = new List<ConfirmedKill>();
            var hits = new List<EnemyHitPresentation>();
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
                AddEnemyHit(new CombatResult
                {
                    EventId = eventId.Value, Damage = tick.Instance.TickDamage,
                    SourcePlayerId = tick.Instance.SourcePlayerId, DamageSourceId = tick.Instance.DamageSourceId,
                    PresentationDamageType = (byte)DamageTypeUtility.FromStatus(tick.Instance.DefinitionId)
                }, applied, hits);
                if (applied.IsConfirmedKill)
                {
                    AddConfirmedKill(applied.Kill, kills);
                    statusAdvance.Changes.AddRange(Statuses.RemoveTarget(applied.State.EntityId));
                }
            }

            return CreateBatch(entities.Values, statusAdvance.Changes, kills, hits);
        }

        private static void AddEnemyHit(CombatResult result, CombatApplyResult applied,
            ICollection<EnemyHitPresentation> hits)
        {
            if (applied.AppliedDamage > 0 && applied.State.Kind == (byte)CombatEntityKind.Enemy)
            {
                hits.Add(new EnemyHitPresentation
                {
                    DamageEventId = result.EventId,
                    Damage = result.Damage,
                    PresentationDamageType = result.PresentationDamageType,
                    IsCritical = result.IsCritical,
                    SourcePlayerId = result.SourcePlayerId,
                    DamageSourceId = result.DamageSourceId,
                    TargetEntityId = applied.State.EntityId,
                    TargetStateVersion = applied.State.StateVersion
                });
            }
        }

        private CanonicalWorldBatch EvidenceCore_HandleSourceDisconnected(
            uint sourcePlayerId,
            double serverTime)
        {
            disconnectedPlayers.Add(sourcePlayerId);
            IReadOnlyList<CanonicalStatusState> changes =
                Statuses.HandleSourceDisconnected(sourcePlayerId, serverTime, StatusDamageAdmissions.GetAcceptedTicks);
            StatusDamageAdmissions.RemovePlayer(sourcePlayerId);
            return CreateBatch(
                Array.Empty<CanonicalEntityState>(),
                changes,
                Array.Empty<ConfirmedKill>());
        }

        /// <summary>Returns all current canonical facts for a newly ready client.</summary>
        private CanonicalWorldBatch EvidenceCore_UnregisterEntity(uint entityId)
        {
            bool retiredEnemy = Ledger.TryGetState(entityId, out var state) &&
                state.Kind == (byte)CombatEntityKind.Enemy;
            IReadOnlyList<CanonicalStatusState> removed = Statuses.RemoveTarget(entityId);
            if (retiredEnemy) { Statuses.ForgetTargetHistory(entityId); retiredEnemies.Add(entityId); }
            Ledger.UnregisterEntity(entityId);
            return CreateBatch(Array.Empty<CanonicalEntityState>(), removed, Array.Empty<ConfirmedKill>());
        }

        private CanonicalWorldBatch EvidenceCore_CreateSnapshot()
        {
            return CreateBatch(
                Ledger.GetAllStates(),
                Statuses.GetAllStates(),
                Array.Empty<ConfirmedKill>());
        }

        private CanonicalWorldBatch EvidenceCore_ResetEnemyCondition(uint entityId)
        {
            var saved = Ledger.CaptureEntityState(entityId);
            var state = saved.State;
            if (!state.Alive || state.Kind != (byte)CombatEntityKind.Enemy)
                throw new InvalidOperationException("Only a living enemy can reset its condition.");
            state.Health = state.MaxHealth;
            state = Ledger.RestoreEntityState(entityId, new ServerEntityCheckpoint(state, saved.AbsoluteInvulnerable));
            return CreateBatch(new[] { state }, Statuses.RemoveTarget(entityId), Array.Empty<ConfirmedKill>());
        }

        /// <summary>Wraps one newly registered entity in the normal sequence stream.</summary>
        private CanonicalWorldBatch EvidenceCore_CreateEntityUpdate(CanonicalEntityState state)
        {
            return CreateBatch(
                new[] { state },
                Array.Empty<CanonicalStatusState>(),
                Array.Empty<ConfirmedKill>());
        }

        private void AddConfirmedKill(ConfirmedKill kill, ICollection<ConfirmedKill> destination)
        {
            if (IsEnemy(kill.TargetEntityId)) enemyDeaths[kill.TargetEntityId] = kill;
            destination.Add(kill);
            Metrics.ConfirmedKills++;
            trace?.RecordConfirmedKill(
                new CombatEventId(kill.CauseEventId),
                kill.KillerPlayerId,
                kill.TargetEntityId,
                kill.TargetStateVersion);
            ConfirmedKillProduced?.Invoke(kill);
        }

        private bool IsEnemy(uint id) => Ledger.TryGetState(id, out var state) &&
            state.Kind == (byte)CombatEntityKind.Enemy;

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
            IEnumerable<ConfirmedKill> kills,
            IEnumerable<EnemyHitPresentation> hits = null)
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
                ConfirmedKills = ToArray(kills),
                EnemyHitPresentations = hits != null ? ToArray(hits) : Array.Empty<EnemyHitPresentation>()
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
