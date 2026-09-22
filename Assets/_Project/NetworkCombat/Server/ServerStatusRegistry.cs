using System;
using System.Collections.Generic;
using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat
{
    public readonly struct StatusMutationResult
    {
        public StatusMutationResult(
            bool accepted,
            CombatRejectionReason rejection,
            CanonicalStatusState state)
        {
            Accepted = accepted;
            Rejection = rejection;
            State = state;
        }

        public bool Accepted { get; }
        public CombatRejectionReason Rejection { get; }
        public CanonicalStatusState State { get; }

        public static StatusMutationResult Reject(CombatRejectionReason reason) =>
            new StatusMutationResult(false, reason, default);
    }

    public readonly struct ServerStatusTick
    {
        public ServerStatusTick(StatusInstance instance, int tickIndex)
        {
            Instance = instance;
            TickIndex = tickIndex;
        }

        public StatusInstance Instance { get; }
        public int TickIndex { get; }
    }

    public sealed class StatusAdvanceResult
    {
        public List<ServerStatusTick> Ticks { get; } = new List<ServerStatusTick>();
        public List<CanonicalStatusState> Changes { get; } =
            new List<CanonicalStatusState>();
    }

    /// <summary>Server canonical Add/Remove/Stack/Duration/Version registry.</summary>
    public sealed partial class ServerStatusRegistry
    {
        private readonly CombatLedger ledger;
        private readonly Dictionary<StatusInstanceId, StatusInstance> instances =
            new Dictionary<StatusInstanceId, StatusInstance>();
        private readonly Dictionary<StatusInstanceId, RemovalRecord> removalVersions =
            new Dictionary<StatusInstanceId, RemovalRecord>();
        private readonly List<StatusInstanceId> removalBuffer = new List<StatusInstanceId>();

        public ServerStatusRegistry(CombatLedger ledger)
        {
            this.ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        }

        public int Count => instances.Count;
        public int RemovalHistoryCount => removalVersions.Count;

        private void EvidenceCore_Clear()
        {
            instances.Clear();
            removalVersions.Clear();
            removalBuffer.Clear();
        }

        /// <summary>Retire history for a target that cannot be restored, such as a despawned Enemy.</summary>
        private void EvidenceCore_ForgetTargetHistory(uint targetEntityId)
        {
            removalBuffer.Clear();
            foreach (var entry in removalVersions)
                if (entry.Value.TargetEntityId == targetEntityId) removalBuffer.Add(entry.Key);
            foreach (var id in removalBuffer) removalVersions.Remove(id);
            removalBuffer.Clear();
        }

        private void RememberRemoval(StatusInstance instance, uint version) =>
            removalVersions[instance.InstanceId] = new RemovalRecord(
                instance.TargetEntityId, version, instance.StartTime + instance.Duration);

        private void PruneRemovalHistory(double serverTime)
        {
            removalBuffer.Clear();
            foreach (var entry in removalVersions)
                if (entry.Value.ExpiresAt <= serverTime) removalBuffer.Add(entry.Key);
            foreach (var id in removalBuffer) removalVersions.Remove(id);
            removalBuffer.Clear();
        }

        private readonly struct RemovalRecord
        {
            public RemovalRecord(uint targetEntityId, uint version, double expiresAt)
            { TargetEntityId = targetEntityId; Version = version; ExpiresAt = expiresAt; }
            public uint TargetEntityId { get; }
            public uint Version { get; }
            public double ExpiresAt { get; }
        }

        public bool Has(uint targetEntityId, EnemyStatusID definitionId)
        {
            foreach (StatusInstance instance in instances.Values)
            {
                if (instance.TargetEntityId == targetEntityId &&
                    instance.DefinitionId == definitionId)
                {
                    return true;
                }
            }

            return false;
        }

        public bool HasFromSource(
            uint targetEntityId,
            EnemyStatusID definitionId,
            uint sourcePlayerId)
        {
            foreach (StatusInstance instance in instances.Values)
            {
                if (instance.TargetEntityId == targetEntityId &&
                    instance.DefinitionId == definitionId &&
                    instance.SourcePlayerId == sourcePlayerId)
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryGet(StatusInstanceId instanceId, out StatusInstance instance)
        {
            return instances.TryGetValue(instanceId, out instance);
        }

        public IReadOnlyList<StatusInstance> GetForTarget(uint targetEntityId)
        {
            var result = new List<StatusInstance>();
            foreach (StatusInstance instance in instances.Values)
            {
                if (instance.TargetEntityId == targetEntityId)
                {
                    result.Add(instance);
                }
            }

            return result;
        }

        /// <summary>Creates a point-in-time copy for late-join synchronization.</summary>
        public IReadOnlyList<CanonicalStatusState> GetAllStates()
        {
            var result = new List<CanonicalStatusState>(instances.Count);
            foreach (StatusInstance instance in instances.Values)
            {
                result.Add(CanonicalStatusState.From(instance));
            }

            return result;
        }

        /// <summary>
        /// Captures only this avatar's live canonical statuses. Absolute server
        /// timing is retained; taking a checkpoint never advances the live runtime.
        /// </summary>
        public CanonicalStatusState[] CaptureTarget(uint targetEntityId, double serverTime)
        {
            ValidateServerTime(serverTime);
            if (targetEntityId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(targetEntityId));
            }

            var result = new List<CanonicalStatusState>();
            foreach (StatusInstance current in instances.Values)
            {
                if (current.TargetEntityId != targetEntityId)
                    continue;

                int completed = CalculateCompletedTicks(current, serverTime);
                if (completed < current.TotalTicks)
                    result.Add(CanonicalStatusState.From(current.WithProgress(completed)));
            }

            result.Sort((left, right) => left.InstanceId.CompareTo(right.InstanceId));
            return result.ToArray();
        }

        /// <summary>
        /// Restores a server-retained avatar checkpoint into the existing status
        /// registry. The caller must detach the previous target before restoration.
        /// Time spent without an avatar consumes duration and tick opportunities;
        /// overdue damage is never replayed against the restored avatar.
        /// </summary>
        public IReadOnlyList<CanonicalStatusState> EvidenceCore_RestoreTarget(
            uint previousTargetEntityId,
            uint targetEntityId,
            IReadOnlyList<CanonicalStatusState> checkpoint,
            double serverTime)
        {
            ValidateServerTime(serverTime);
            if (previousTargetEntityId == 0 || targetEntityId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(targetEntityId));
            }

            if (checkpoint == null)
            {
                throw new ArgumentNullException(nameof(checkpoint));
            }

            if (!ledger.TryGetState(targetEntityId, out CanonicalEntityState target))
            {
                throw new InvalidOperationException("Register the avatar before restoring statuses.");
            }

            var restored = new List<StatusInstance>();
            var changes = new List<CanonicalStatusState>();
            var seen = new HashSet<StatusInstanceId>();
            for (int i = 0; i < checkpoint.Count; i++)
            {
                CanonicalStatusState saved = checkpoint[i];
                if (saved.Removed || saved.TargetEntityId != previousTargetEntityId)
                {
                    throw new ArgumentException("The status checkpoint belongs to another avatar.",
                        nameof(checkpoint));
                }

                StatusInstance original = saved.ToStatusInstance();
                if (!seen.Add(original.InstanceId))
                {
                    throw new ArgumentException("The status checkpoint contains duplicate instances.",
                        nameof(checkpoint));
                }

                int completed = CalculateCompletedTicks(original, serverTime);
                if (completed >= original.TotalTicks)
                    continue;

                if (instances.TryGetValue(original.InstanceId, out StatusInstance current))
                {
                    if (current.TargetEntityId != targetEntityId)
                    {
                        throw new InvalidOperationException(
                            "Detach the previous avatar before restoring its status instances.");
                    }

                    // Repeating an already-applied binding must not rewind ticks.
                    changes.Add(CanonicalStatusState.From(current));
                    continue;
                }

                bool selfSource = saved.SourcePlayerId == previousTargetEntityId;
                if (saved.ExecutionAuthority == (byte)StatusExecutionAuthority.SourceClient &&
                    (selfSource || !ledger.IsSourceOwnedBy(saved.SourceEntityId, saved.SourcePlayerId)))
                {
                    // Preserve the established disconnect takeover policy. Old
                    // attacks do not regain source execution on a new connection.
                    saved.ExecutionAuthority = (byte)StatusExecutionAuthority.Server;
                }

                if (selfSource)
                    saved.SourcePlayerId = target.OwnerPlayerId != 0 ? target.OwnerPlayerId : targetEntityId;
                if (saved.SourceEntityId == previousTargetEntityId)
                    saved.SourceEntityId = targetEntityId;
                if (saved.DamageSourceId == previousTargetEntityId)
                    saved.DamageSourceId = targetEntityId;
                saved.TargetEntityId = targetEntityId;
                saved.CompletedTicks = completed;
                removalVersions.TryGetValue(original.InstanceId, out RemovalRecord removal);
                saved.Version = checked(Math.Max(saved.Version, removal.Version) + 1u);

                StatusInstance instance = saved.ToStatusInstance();
                restored.Add(instance);
                changes.Add(CanonicalStatusState.From(instance));
            }

            // Validate the whole checkpoint before mutating this player's runtime.
            for (int i = 0; i < restored.Count; i++)
                instances.Add(restored[i].InstanceId, restored[i]);

            return changes;
        }

        private StatusMutationResult EvidenceCore_Apply(
            uint senderPlayerId,
            StatusMutation mutation,
            double serverTime)
        {
            if (double.IsNaN(serverTime) || double.IsInfinity(serverTime))
            {
                throw new ArgumentOutOfRangeException(nameof(serverTime));
            }

            CombatRejectionReason validation = Validate(senderPlayerId, mutation);
            if (validation != CombatRejectionReason.None)
            {
                return StatusMutationResult.Reject(validation);
            }

            var instanceId = new StatusInstanceId(mutation.InstanceId);
            if (mutation.ApplicationRevision == 0) return StatusMutationResult.Reject(CombatRejectionReason.InvalidStatus);
            if (mutation.Kind == StatusMutationKind.Remove)
            {
                if (!instances.TryGetValue(instanceId, out StatusInstance removed))
                {
                    return StatusMutationResult.Reject(CombatRejectionReason.InvalidStatus);
                }
                if (mutation.ApplicationRevision != removed.ApplicationRevision)
                    return StatusMutationResult.Reject(CombatRejectionReason.InvalidStatus);

                uint version = removed.Version + 1;
                instances.Remove(instanceId);
                RememberRemoval(removed, version);
                return new StatusMutationResult(
                    true,
                    CombatRejectionReason.None,
                    CanonicalStatusState.Removal(removed, version));
            }

            if (!instances.TryGetValue(instanceId, out StatusInstance current))
            {
                if (mutation.StackDelta < 1)
                {
                    return StatusMutationResult.Reject(CombatRejectionReason.InvalidStatus);
                }

                StatusInstance added = CreateFromMutation(
                    mutation,
                    Math.Min(mutation.StackDelta, mutation.MaxStacks),
                    version: 1,
                    serverTime: serverTime);
                instances.Add(instanceId, added);
                return new StatusMutationResult(
                    true,
                    CombatRejectionReason.None,
                    CanonicalStatusState.From(added));
            }

            if (current.SourcePlayerId != mutation.SourcePlayerId ||
                current.SourceEntityId != mutation.SourceEntityId ||
                current.TargetEntityId != mutation.TargetEntityId ||
                (uint)current.DefinitionId != mutation.DefinitionId)
            {
                return StatusMutationResult.Reject(CombatRejectionReason.InvalidStatus);
            }
            if (mutation.ApplicationRevision < current.ApplicationRevision)
                return StatusMutationResult.Reject(CombatRejectionReason.InvalidStatus);
            if (mutation.ApplicationRevision == current.ApplicationRevision &&
                (mutation.TotalTicks != current.TotalTicks || mutation.TickInterval != current.TickInterval ||
                 mutation.TickDamage != current.TickDamage || mutation.Duration != current.Duration))
                return StatusMutationResult.Reject(CombatRejectionReason.InvalidStatus);

            int nextStack = current.Stack;
            switch (current.Definition.StackMode)
            {
                case StatusStackMode.Add:
                    nextStack += mutation.StackDelta;
                    break;
                case StatusStackMode.Replace:
                    nextStack = mutation.StackDelta > 0 ? mutation.StackDelta : current.Stack;
                    break;
                case StatusStackMode.HighestPriority:
                    if (mutation.Priority < current.Priority)
                    {
                        return StatusMutationResult.Reject(CombatRejectionReason.InvalidStatus);
                    }

                    nextStack = mutation.StackDelta > 0 ? mutation.StackDelta : current.Stack;
                    break;
                default:
                    return StatusMutationResult.Reject(CombatRejectionReason.InvalidStatus);
            }

            if (nextStack <= 0)
            {
                uint removedVersion = current.Version + 1;
                instances.Remove(instanceId);
                RememberRemoval(current, removedVersion);
                return new StatusMutationResult(
                    true,
                    CombatRejectionReason.None,
                    CanonicalStatusState.Removal(current, removedVersion));
            }

            if (nextStack > current.Definition.MaxStacks)
            {
                return StatusMutationResult.Reject(CombatRejectionReason.InvalidStatus);
            }

            StatusInstance updated = CreateFromMutation(
                mutation,
                nextStack,
                current.Version + 1,
                serverTime);
            if (mutation.ApplicationRevision == current.ApplicationRevision)
            {
                // Stack corrections do not start another timer or grant another tick budget.
                updated = new StatusInstance(updated.InstanceId, updated.Definition, updated.SourcePlayerId,
                    updated.SourceEntityId, updated.TargetEntityId, updated.Stack, current.StartTime,
                    current.Duration, current.ExecutionAuthority, updated.Version, current.TickDamage,
                    current.TotalTicks, Math.Max(current.CompletedTicks, mutation.CompletedTicks), current.TickInterval,
                    current.Priority, current.DamageSourceId, updated.SourceContext, updated.Magnitude, current.ApplicationRevision);
            }
            instances[instanceId] = updated;
            return new StatusMutationResult(
                true,
                CombatRejectionReason.None,
                CanonicalStatusState.From(updated));
        }

        private CanonicalStatusState EvidenceCore_AddServerStatus(StatusInstance instance)
        {
            if (instance.ExecutionAuthority != StatusExecutionAuthority.Server)
            {
                throw new ArgumentException(
                    "Server-created statuses must use Server execution authority.",
                    nameof(instance));
            }

            instances[instance.InstanceId] = instance;
            return CanonicalStatusState.From(instance);
        }

        public IReadOnlyList<CanonicalStatusState> EvidenceCore_HandleSourceDisconnected(
            uint sourcePlayerId,
            double serverTime,
            Func<StatusInstance, int> acceptedTicks = null)
        {
            var changes = new List<CanonicalStatusState>();
            var ids = new List<StatusInstanceId>(instances.Keys);
            for (int i = 0; i < ids.Count; i++)
            {
                StatusInstance current = instances[ids[i]];
                if (current.SourcePlayerId != sourcePlayerId ||
                    current.ExecutionAuthority != StatusExecutionAuthority.SourceClient)
                {
                    continue;
                }

                int completed = Math.Max(CalculateCompletedTicks(current, serverTime), acceptedTicks?.Invoke(current) ?? 0);
                if (completed >= current.TotalTicks)
                {
                    uint removalVersion = current.Version + 1;
                    instances.Remove(current.InstanceId);
                    RememberRemoval(current, removalVersion);
                    changes.Add(CanonicalStatusState.Removal(
                        current,
                        removalVersion));
                    continue;
                }

                StatusInstance failover = Copy(
                    current,
                    StatusExecutionAuthority.Server,
                    current.Version + 1,
                    completed);
                instances[current.InstanceId] = failover;
                changes.Add(CanonicalStatusState.From(failover));
            }

            return changes;
        }

        public IReadOnlyList<CanonicalStatusState> EvidenceCore_RemoveTarget(uint targetEntityId)
        {
            var changes = new List<CanonicalStatusState>();
            var ids = new List<StatusInstanceId>(instances.Keys);
            for (int i = 0; i < ids.Count; i++)
            {
                StatusInstance current = instances[ids[i]];
                if (current.TargetEntityId != targetEntityId)
                {
                    continue;
                }

                uint removalVersion = current.Version + 1;
                instances.Remove(current.InstanceId);
                RememberRemoval(current, removalVersion);
                changes.Add(CanonicalStatusState.Removal(
                    current,
                    removalVersion));
            }

            return changes;
        }

        private StatusAdvanceResult EvidenceCore_Advance(double serverTime)
        {
            if (double.IsNaN(serverTime) || double.IsInfinity(serverTime))
            {
                throw new ArgumentOutOfRangeException(nameof(serverTime));
            }

            var result = new StatusAdvanceResult();
            var ids = new List<StatusInstanceId>(instances.Keys);
            ids.Sort((left, right) => left.Value.CompareTo(right.Value));

            for (int i = 0; i < ids.Count; i++)
            {
                StatusInstance current = instances[ids[i]];
                int expectedCompleted = CalculateCompletedTicks(current, serverTime);
                if (current.ExecutionAuthority == StatusExecutionAuthority.Server)
                {
                    for (int tick = current.CompletedTicks + 1; tick <= expectedCompleted; tick++)
                    {
                        result.Ticks.Add(new ServerStatusTick(current, tick));
                    }

                    if (expectedCompleted != current.CompletedTicks &&
                        expectedCompleted < current.TotalTicks)
                    {
                        instances[current.InstanceId] = current.WithProgress(expectedCompleted);
                    }
                }

                if (expectedCompleted >= current.TotalTicks)
                {
                    uint removalVersion = current.Version + 1;
                    instances.Remove(current.InstanceId);
                    RememberRemoval(current, removalVersion);
                    result.Changes.Add(CanonicalStatusState.Removal(
                        current,
                        removalVersion));
                }
            }

            // RestoreTarget skips checkpoints once their original duration has elapsed.
            PruneRemovalHistory(serverTime);
            return result;
        }

        private CombatRejectionReason Validate(uint senderPlayerId, StatusMutation mutation)
        {
            if (senderPlayerId == 0 || mutation.SourcePlayerId != senderPlayerId)
            {
                return CombatRejectionReason.InvalidSender;
            }

            if (mutation.EventId == 0 || mutation.Sequence == 0 || mutation.InstanceId == 0)
            {
                return CombatRejectionReason.InvalidSequence;
            }

            if (!ledger.IsSourceOwnedBy(mutation.SourceEntityId, senderPlayerId))
            {
                return CombatRejectionReason.SourceNotOwned;
            }

            bool clientFinalEnemy = ledger.TryGetState(mutation.TargetEntityId, out var target) &&
                target.Kind == (byte)CombatEntityKind.Enemy;
            if (!clientFinalEnemy && ledger.IsPlayerSelectingUpgrade(senderPlayerId))
            {
                return CombatRejectionReason.SourceSelectingUpgrade;
            }

            if (!ledger.IsAlive(mutation.TargetEntityId))
            {
                return CombatRejectionReason.TargetCanonicalDead;
            }

            if (mutation.ExecutionAuthority != (byte)StatusExecutionAuthority.SourceClient ||
                mutation.DefinitionId == 0 ||
                mutation.MaxStacks < 1 ||
                mutation.StackMode > (byte)StatusStackMode.HighestPriority ||
                mutation.TickDamage < 0 ||
                (!clientFinalEnemy && mutation.TickDamage > ledger.MaximumDamagePerResult) ||
                mutation.TotalTicks < 1 ||
                mutation.CompletedTicks < 0 ||
                mutation.CompletedTicks > mutation.TotalTicks ||
                float.IsNaN(mutation.TickInterval) ||
                float.IsInfinity(mutation.TickInterval) ||
                mutation.TickInterval <= 0 ||
                float.IsNaN(mutation.Duration) ||
                float.IsInfinity(mutation.Duration) ||
                mutation.Duration <= 0 ||
                double.IsNaN(mutation.StartTime) ||
                double.IsInfinity(mutation.StartTime) ||
                float.IsNaN(mutation.Priority) ||
                float.IsInfinity(mutation.Priority) ||
                float.IsNaN(mutation.Magnitude) ||
                float.IsInfinity(mutation.Magnitude))
            {
                return CombatRejectionReason.InvalidStatus;
            }

            return CombatRejectionReason.None;
        }

        private static StatusInstance CreateFromMutation(
            StatusMutation mutation,
            int stack,
            uint version,
            double serverTime)
        {
            CombatContext sourceContext = mutation.EventId != 0UL
                ? new CombatContext(
                    new CombatEventId(mutation.EventId),
                    new CombatEventId(
                        mutation.RootEventId != 0UL
                            ? mutation.RootEventId
                            : mutation.EventId),
                    new CombatEventId(mutation.ParentEventId),
                    mutation.Sequence,
                    mutation.ChainDepth,
                    mutation.SourcePlayerId,
                    mutation.SourceEntityId,
                    mutation.TargetEntityId,
                    mutation.AbilityId,
                    mutation.BuildId,
                    (CombatTags)mutation.Tags,
                    mutation.TargetStateVersion)
                : default;
            return new StatusInstance(
                new StatusInstanceId(mutation.InstanceId),
                new StatusDefinition(
                    (EnemyStatusID)mutation.DefinitionId,
                    (StatusStackMode)mutation.StackMode,
                    mutation.MaxStacks),
                mutation.SourcePlayerId,
                mutation.SourceEntityId,
                mutation.TargetEntityId,
                stack,
                serverTime - mutation.CompletedTicks * mutation.TickInterval,
                mutation.Duration,
                (StatusExecutionAuthority)mutation.ExecutionAuthority,
                version,
                mutation.TickDamage,
                mutation.TotalTicks,
                mutation.CompletedTicks,
                mutation.TickInterval,
                mutation.Priority,
                mutation.DamageSourceId,
                sourceContext,
                mutation.Magnitude,
                mutation.ApplicationRevision);
        }

        private static int CalculateCompletedTicks(StatusInstance instance, double serverTime)
        {
            if (serverTime <= instance.StartTime)
            {
                return instance.CompletedTicks;
            }

            double elapsed = serverTime - instance.StartTime;
            int byTime = elapsed >= instance.Duration
                ? instance.TotalTicks
                : (int)Math.Floor((elapsed + 0.000001d) / instance.TickInterval);
            if (byTime < instance.CompletedTicks)
            {
                byTime = instance.CompletedTicks;
            }

            return Math.Min(byTime, instance.TotalTicks);
        }

        private static void ValidateServerTime(double serverTime)
        {
            if (double.IsNaN(serverTime) || double.IsInfinity(serverTime))
                throw new ArgumentOutOfRangeException(nameof(serverTime));
        }

        private static StatusInstance Copy(
            StatusInstance source,
            StatusExecutionAuthority authority,
            uint version,
            int completedTicks)
        {
            return new StatusInstance(
                source.InstanceId,
                source.Definition,
                source.SourcePlayerId,
                source.SourceEntityId,
                source.TargetEntityId,
                source.Stack,
                source.StartTime,
                source.Duration,
                authority,
                version,
                source.TickDamage,
                source.TotalTicks,
                completedTicks,
                source.TickInterval,
                source.Priority,
                source.DamageSourceId,
                source.SourceContext,
                source.Magnitude,
                source.ApplicationRevision);
        }
    }
}
