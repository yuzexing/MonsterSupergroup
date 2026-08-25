using System;
using System.Collections.Generic;

namespace MonsterSupergroup.GAS
{
    /// <summary>
    /// The single GAS status runtime. It owns effective query state, local prediction,
    /// canonical reconciliation and tick execution gating.
    /// </summary>
    public sealed class StatusController
    {
        private const float TimeEpsilon = 0.000001f;

        private readonly Action<StatusTick> tickReceiver;
        private readonly IStatusInstanceIdSource instanceIds;
        private readonly Dictionary<EnemyStatusID, List<ActiveStatus>> activeStatuses =
            new Dictionary<EnemyStatusID, List<ActiveStatus>>();
        private readonly Dictionary<StatusInstanceId, uint> removalVersions =
            new Dictionary<StatusInstanceId, uint>();
        private readonly List<StatusTick> pendingTicks = new List<StatusTick>();
        private readonly List<StatusChange> pendingChanges = new List<StatusChange>();
        private readonly List<EnemyStatusID> emptyStatusIds = new List<EnemyStatusID>();
        private readonly List<EnemyStatusID> statusIdBuffer = new List<EnemyStatusID>();

        private IStatusExecutionPolicy executionPolicy;
        private double currentTime;

        public StatusController(Action<StatusTick> tickReceiver)
            : this(
                tickReceiver,
                new SequentialStatusInstanceIdSource(),
                ExecuteAllStatusPolicy.Instance)
        {
        }

        public StatusController(
            Action<StatusTick> tickReceiver,
            IStatusInstanceIdSource instanceIds,
            IStatusExecutionPolicy executionPolicy)
        {
            this.tickReceiver = tickReceiver ?? throw new ArgumentNullException(nameof(tickReceiver));
            this.instanceIds = instanceIds ?? throw new ArgumentNullException(nameof(instanceIds));
            this.executionPolicy = executionPolicy ?? throw new ArgumentNullException(nameof(executionPolicy));
        }

        public event Action<StatusChange> Changed;

        public double CurrentTime => currentTime;

        public int Count
        {
            get
            {
                int count = 0;
                foreach (List<ActiveStatus> statuses in activeStatuses.Values)
                {
                    for (int i = 0; i < statuses.Count; i++)
                    {
                        if (statuses[i].EffectiveStack > 0)
                        {
                            count++;
                        }
                    }
                }

                return count;
            }
        }

        public void SetExecutionPolicy(IStatusExecutionPolicy policy)
        {
            executionPolicy = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        public bool Has(EnemyStatusID statusId)
        {
            return GetStackCount(statusId) > 0;
        }

        public bool HasFromSource(EnemyStatusID statusId, uint sourcePlayerId)
        {
            if (!activeStatuses.TryGetValue(statusId, out List<ActiveStatus> statuses))
            {
                return false;
            }

            for (int i = 0; i < statuses.Count; i++)
            {
                if (statuses[i].EffectiveStack > 0 &&
                    statuses[i].Instance.SourcePlayerId == sourcePlayerId)
                {
                    return true;
                }
            }

            return false;
        }

        public int GetStackCount(EnemyStatusID statusId)
        {
            if (!activeStatuses.TryGetValue(statusId, out List<ActiveStatus> statuses))
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < statuses.Count; i++)
            {
                count += statuses[i].EffectiveStack;
            }

            return count;
        }

        public int GetCanonicalStackCount(EnemyStatusID statusId)
        {
            if (!activeStatuses.TryGetValue(statusId, out List<ActiveStatus> statuses))
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < statuses.Count; i++)
            {
                count += statuses[i].CanonicalStack;
            }

            return count;
        }

        public int GetPredictedStackDelta(EnemyStatusID statusId)
        {
            if (!activeStatuses.TryGetValue(statusId, out List<ActiveStatus> statuses))
            {
                return 0;
            }

            int delta = 0;
            for (int i = 0; i < statuses.Count; i++)
            {
                delta += statuses[i].PredictedStackDelta;
            }

            return delta;
        }

        public IReadOnlyList<StatusInstance> GetInstances(EnemyStatusID statusId)
        {
            if (!activeStatuses.TryGetValue(statusId, out List<ActiveStatus> statuses))
            {
                return Array.Empty<StatusInstance>();
            }

            var result = new List<StatusInstance>(statuses.Count);
            for (int i = 0; i < statuses.Count; i++)
            {
                if (statuses[i].EffectiveStack > 0)
                {
                    result.Add(statuses[i].EffectiveInstance);
                }
            }

            return result;
        }

        public bool TryGet(StatusInstanceId instanceId, out StatusInstance instance)
        {
            if (TryFind(instanceId, out _, out ActiveStatus active) && active.EffectiveStack > 0)
            {
                instance = active.EffectiveInstance;
                return true;
            }

            instance = default;
            return false;
        }

        /// <summary>
        /// Applies an owner-client prediction. The generated instance ID is stable and
        /// is later reused by the server's canonical replica.
        /// </summary>
        public StatusApplicationResult Apply(StatusApplication application)
        {
            if (application.Definition.Id == EnemyStatusID.None)
            {
                throw new ArgumentException(
                    "Status application must use a valid non-zero definition.",
                    nameof(application));
            }

            EnemyStatusID id = application.Definition.Id;
            if (!activeStatuses.TryGetValue(id, out List<ActiveStatus> statuses))
            {
                statuses = new List<ActiveStatus>(application.Definition.MaxStacks);
                activeStatuses.Add(id, statuses);
            }

            ValidateCompatibleDefinition(statuses, application.Definition);
            StatusInstanceId incomingId = application.InstanceId.IsValid
                ? application.InstanceId
                : instanceIds.Next();

            if (TryFindInList(statuses, incomingId, out ActiveStatus sameInstance))
            {
                return RefreshExistingPrediction(sameInstance, application);
            }

            switch (application.Definition.StackMode)
            {
                case StatusStackMode.Add:
                    if (GetStackCount(id) + application.Stack > application.Definition.MaxStacks)
                    {
                        return StatusApplicationResult.Rejected;
                    }

                    ActiveStatus added = ActiveStatus.FromPredictedApplication(
                        application,
                        incomingId,
                        ResolveStartTime(application));
                    statuses.Add(added);
                    Publish(StatusChangeKind.Added, StatusStateOrigin.Predicted, added);
                    return StatusApplicationResult.Added;

                case StatusStackMode.Replace:
                    RemoveAll(statuses, StatusStateOrigin.Predicted, StatusRemovalReason.Reconciled);
                    ActiveStatus replacement = ActiveStatus.FromPredictedApplication(
                        application,
                        incomingId,
                        ResolveStartTime(application));
                    statuses.Add(replacement);
                    Publish(StatusChangeKind.Added, StatusStateOrigin.Predicted, replacement);
                    return StatusApplicationResult.Replaced;

                case StatusStackMode.HighestPriority:
                    ActiveStatus current = FirstEffective(statuses);
                    if (current == null)
                    {
                        ActiveStatus first = ActiveStatus.FromPredictedApplication(
                            application,
                            incomingId,
                            ResolveStartTime(application));
                        statuses.Add(first);
                        Publish(StatusChangeKind.Added, StatusStateOrigin.Predicted, first);
                        return StatusApplicationResult.Added;
                    }

                    if (application.Priority < current.Instance.Priority)
                    {
                        return StatusApplicationResult.Rejected;
                    }

                    bool isHigher = application.Priority > current.Instance.Priority;
                    current.RefreshPrediction(application, ResolveStartTime(application));
                    Publish(StatusChangeKind.Updated, StatusStateOrigin.Predicted, current);
                    return isHigher
                        ? StatusApplicationResult.Replaced
                        : StatusApplicationResult.Refreshed;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public bool ApplyPredictedStackDelta(StatusInstanceId instanceId, int stackDelta)
        {
            if (stackDelta == 0 ||
                !TryFind(instanceId, out List<ActiveStatus> statuses, out ActiveStatus active))
            {
                return false;
            }

            int nextStack = active.EffectiveStack + stackDelta;
            if (nextStack < 0 || nextStack > active.Instance.Definition.MaxStacks)
            {
                return false;
            }

            StatusInstance prior = active.EffectiveStack > 0
                ? active.EffectiveInstance
                : active.Instance;
            active.PredictedStackDelta += stackDelta;
            if (active.EffectiveStack == 0)
            {
                Changed?.Invoke(new StatusChange(
                    StatusChangeKind.Removed,
                    StatusStateOrigin.Predicted,
                    prior,
                    StatusRemovalReason.Consumed));
                if (active.CanonicalStack == 0)
                {
                    statuses.Remove(active);
                    RemoveEmptyList(active.Instance.DefinitionId, statuses);
                }
            }
            else
            {
                Publish(StatusChangeKind.Updated, StatusStateOrigin.Predicted, active);
            }

            return true;
        }

        /// <summary>
        /// Reconciles one server snapshot. Matching prediction is cleared instead of
        /// replaying status gameplay.
        /// </summary>
        public bool UpsertCanonical(StatusInstance snapshot)
        {
            if (removalVersions.TryGetValue(snapshot.InstanceId, out uint removalVersion) &&
                snapshot.Version <= removalVersion)
            {
                return false;
            }

            if (TryFind(snapshot.InstanceId, out List<ActiveStatus> existingList, out ActiveStatus existing))
            {
                if (existing.CanonicalVersion > snapshot.Version)
                {
                    return false;
                }

                existing.ApplyCanonical(snapshot);
                Publish(StatusChangeKind.Updated, StatusStateOrigin.Canonical, existing);
                return true;
            }

            if (!activeStatuses.TryGetValue(snapshot.DefinitionId, out List<ActiveStatus> statuses))
            {
                statuses = new List<ActiveStatus>(snapshot.Definition.MaxStacks);
                activeStatuses.Add(snapshot.DefinitionId, statuses);
            }

            ValidateCompatibleDefinition(statuses, snapshot.Definition);
            var added = ActiveStatus.FromCanonical(snapshot);
            statuses.Add(added);
            Publish(StatusChangeKind.Added, StatusStateOrigin.Canonical, added);
            return true;
        }

        public bool RemoveCanonical(StatusInstanceId instanceId, uint version)
        {
            if (version == 0u)
            {
                throw new ArgumentOutOfRangeException(nameof(version));
            }

            if (removalVersions.TryGetValue(instanceId, out uint knownVersion) &&
                version <= knownVersion)
            {
                return false;
            }

            removalVersions[instanceId] = version;
            if (!TryFind(instanceId, out List<ActiveStatus> statuses, out ActiveStatus active))
            {
                return true;
            }

            if (active.CanonicalVersion > version)
            {
                return false;
            }

            StatusInstance removed = active.EffectiveStack > 0
                ? active.EffectiveInstance
                : active.Instance;
            statuses.Remove(active);
            RemoveEmptyList(active.Instance.DefinitionId, statuses);
            Changed?.Invoke(new StatusChange(
                StatusChangeKind.Removed,
                StatusStateOrigin.Canonical,
                removed,
                StatusRemovalReason.Reconciled));
            return true;
        }

        public void Advance(float deltaSeconds)
        {
            if (float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds) || deltaSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(deltaSeconds),
                    "Delta time must be finite and non-negative.");
            }

            if (deltaSeconds == 0f)
            {
                return;
            }

            currentTime += deltaSeconds;
            if (activeStatuses.Count == 0)
            {
                return;
            }

            pendingTicks.Clear();
            pendingChanges.Clear();
            emptyStatusIds.Clear();
            statusIdBuffer.Clear();
            statusIdBuffer.AddRange(activeStatuses.Keys);
            statusIdBuffer.Sort();

            for (int statusIdIndex = 0; statusIdIndex < statusIdBuffer.Count; statusIdIndex++)
            {
                EnemyStatusID statusId = statusIdBuffer[statusIdIndex];
                List<ActiveStatus> statuses = activeStatuses[statusId];
                for (int i = 0; i < statuses.Count; i++)
                {
                    ActiveStatus status = statuses[i];
                    if (status.EffectiveStack == 0)
                    {
                        continue;
                    }

                    status.Elapsed += deltaSeconds;
                    while (status.RemainingHits > 0 &&
                           status.Elapsed + TimeEpsilon >= status.Instance.TickInterval)
                    {
                        status.Elapsed -= status.Instance.TickInterval;
                        if (status.Elapsed < 0f && status.Elapsed > -TimeEpsilon)
                        {
                            status.Elapsed = 0f;
                        }

                        status.RemainingHits--;
                        status.CompletedTicks++;
                        StatusInstance effective = status.EffectiveInstance.WithProgress(
                            status.CompletedTicks);
                        if (executionPolicy.CanExecute(effective))
                        {
                            pendingTicks.Add(new StatusTick(
                                effective,
                                new DamageInfo(
                                    effective.DamageSourceId,
                                    effective.TickDamage,
                                    false),
                                status.CompletedTicks,
                                status.RemainingHits == 0));
                        }
                    }

                    if (status.RemainingHits == 0)
                    {
                        StatusInstance expired = status.EffectiveInstance.WithProgress(
                            status.CompletedTicks);
                        pendingChanges.Add(new StatusChange(
                            StatusChangeKind.Removed,
                            status.CanonicalStack > 0
                                ? StatusStateOrigin.Canonical
                                : StatusStateOrigin.Predicted,
                            expired,
                            StatusRemovalReason.Expired));
                        statuses.RemoveAt(i);
                        i--;
                    }
                }

                if (statuses.Count == 0)
                {
                    emptyStatusIds.Add(statusId);
                }
            }

            for (int i = 0; i < emptyStatusIds.Count; i++)
            {
                activeStatuses.Remove(emptyStatusIds[i]);
            }

            // Dispatch only after collection mutation. Receivers may apply chained statuses.
            for (int i = 0; i < pendingChanges.Count; i++)
            {
                Changed?.Invoke(pendingChanges[i]);
            }

            for (int i = 0; i < pendingTicks.Count; i++)
            {
                tickReceiver(pendingTicks[i]);
            }
        }

        public bool Consume(EnemyStatusID statusId)
        {
            if (!activeStatuses.TryGetValue(statusId, out List<ActiveStatus> statuses))
            {
                return false;
            }

            ActiveStatus active = FirstEffective(statuses);
            if (active == null)
            {
                return false;
            }

            if (active.EffectiveStack > 1)
            {
                return ApplyPredictedStackDelta(active.Instance.InstanceId, -1);
            }

            StatusInstance removed = active.EffectiveInstance;
            statuses.Remove(active);
            RemoveEmptyList(statusId, statuses);
            Changed?.Invoke(new StatusChange(
                StatusChangeKind.Removed,
                StatusStateOrigin.Predicted,
                removed,
                StatusRemovalReason.Consumed));
            return true;
        }

        public bool Clear(EnemyStatusID statusId)
        {
            if (!activeStatuses.TryGetValue(statusId, out List<ActiveStatus> statuses))
            {
                return false;
            }

            RemoveAll(statuses, StatusStateOrigin.Predicted, StatusRemovalReason.Cleared);
            activeStatuses.Remove(statusId);
            return true;
        }

        public void Clear()
        {
            var changes = new List<StatusChange>();
            foreach (List<ActiveStatus> statuses in activeStatuses.Values)
            {
                for (int i = 0; i < statuses.Count; i++)
                {
                    if (statuses[i].EffectiveStack > 0)
                    {
                        changes.Add(new StatusChange(
                            StatusChangeKind.Removed,
                            StatusStateOrigin.Predicted,
                            statuses[i].EffectiveInstance,
                            StatusRemovalReason.Cleared));
                    }
                }
            }

            activeStatuses.Clear();
            removalVersions.Clear();
            for (int i = 0; i < changes.Count; i++)
            {
                Changed?.Invoke(changes[i]);
            }
        }

        private StatusApplicationResult RefreshExistingPrediction(
            ActiveStatus active,
            StatusApplication application)
        {
            if (application.Priority < active.Instance.Priority)
            {
                return StatusApplicationResult.Rejected;
            }

            bool replaced = application.Priority > active.Instance.Priority;
            active.RefreshPrediction(application, ResolveStartTime(application));
            Publish(StatusChangeKind.Updated, StatusStateOrigin.Predicted, active);
            return replaced ? StatusApplicationResult.Replaced : StatusApplicationResult.Refreshed;
        }

        private double ResolveStartTime(StatusApplication application)
        {
            return application.HasExplicitStartTime ? application.StartTime : currentTime;
        }

        private void Publish(StatusChangeKind kind, StatusStateOrigin origin, ActiveStatus active)
        {
            if (active.EffectiveStack > 0)
            {
                Changed?.Invoke(new StatusChange(kind, origin, active.EffectiveInstance));
            }
        }

        private void RemoveAll(
            List<ActiveStatus> statuses,
            StatusStateOrigin origin,
            StatusRemovalReason reason)
        {
            for (int i = 0; i < statuses.Count; i++)
            {
                if (statuses[i].EffectiveStack > 0)
                {
                    Changed?.Invoke(new StatusChange(
                        StatusChangeKind.Removed,
                        origin,
                        statuses[i].EffectiveInstance,
                        reason));
                }
            }

            statuses.Clear();
        }

        private bool TryFind(
            StatusInstanceId instanceId,
            out List<ActiveStatus> owner,
            out ActiveStatus result)
        {
            foreach (List<ActiveStatus> statuses in activeStatuses.Values)
            {
                if (TryFindInList(statuses, instanceId, out result))
                {
                    owner = statuses;
                    return true;
                }
            }

            owner = null;
            result = null;
            return false;
        }

        private static bool TryFindInList(
            List<ActiveStatus> statuses,
            StatusInstanceId instanceId,
            out ActiveStatus result)
        {
            for (int i = 0; i < statuses.Count; i++)
            {
                if (statuses[i].Instance.InstanceId == instanceId)
                {
                    result = statuses[i];
                    return true;
                }
            }

            result = null;
            return false;
        }

        private static ActiveStatus FirstEffective(List<ActiveStatus> statuses)
        {
            for (int i = 0; i < statuses.Count; i++)
            {
                if (statuses[i].EffectiveStack > 0)
                {
                    return statuses[i];
                }
            }

            return null;
        }

        private void RemoveEmptyList(EnemyStatusID statusId, List<ActiveStatus> statuses)
        {
            if (statuses.Count == 0)
            {
                activeStatuses.Remove(statusId);
            }
        }

        private static void ValidateCompatibleDefinition(
            IReadOnlyList<ActiveStatus> statuses,
            StatusDefinition definition)
        {
            if (statuses.Count > 0 && !statuses[0].Instance.Definition.Equals(definition))
            {
                throw new InvalidOperationException(
                    $"Status {definition.Id} was applied with a definition that differs from its active definition.");
            }
        }

        private sealed class ActiveStatus
        {
            private ActiveStatus(
                StatusInstance instance,
                int canonicalStack,
                int predictedStackDelta,
                uint canonicalVersion)
            {
                Instance = instance;
                CanonicalStack = canonicalStack;
                PredictedStackDelta = predictedStackDelta;
                CanonicalVersion = canonicalVersion;
                CompletedTicks = instance.CompletedTicks;
                RemainingHits = instance.RemainingTicks;
                Elapsed = 0f;
            }

            public StatusInstance Instance { get; private set; }
            public int CanonicalStack { get; private set; }
            public int PredictedStackDelta { get; set; }
            public uint CanonicalVersion { get; private set; }
            public int RemainingHits { get; set; }
            public int CompletedTicks { get; set; }
            public float Elapsed { get; set; }

            public int EffectiveStack
            {
                get
                {
                    int value = CanonicalStack + PredictedStackDelta;
                    if (value <= 0)
                    {
                        return 0;
                    }

                    return value > Instance.Definition.MaxStacks
                        ? Instance.Definition.MaxStacks
                        : value;
                }
            }

            public StatusInstance EffectiveInstance => Instance
                .WithStack(EffectiveStack)
                .WithProgress(CompletedTicks);

            public static ActiveStatus FromPredictedApplication(
                StatusApplication application,
                StatusInstanceId instanceId,
                double startTime)
            {
                StatusInstance instance = CreateInstance(
                    application,
                    instanceId,
                    startTime,
                    version: 1,
                    completedTicks: 0);
                return new ActiveStatus(instance, 0, application.Stack, 0);
            }

            public static ActiveStatus FromCanonical(StatusInstance instance)
            {
                return new ActiveStatus(instance, instance.Stack, 0, instance.Version);
            }

            public void RefreshPrediction(StatusApplication application, double startTime)
            {
                int predictedStack = application.Stack - CanonicalStack;
                Instance = CreateInstance(
                    application,
                    Instance.InstanceId,
                    startTime,
                    CanonicalVersion > 0 ? CanonicalVersion : 1,
                    completedTicks: 0);
                PredictedStackDelta = predictedStack;
                RemainingHits = application.NumberOfHits;
                CompletedTicks = 0;
                Elapsed = 0f;
            }

            public void ApplyCanonical(StatusInstance snapshot)
            {
                Instance = snapshot;
                CanonicalStack = snapshot.Stack;
                PredictedStackDelta = 0;
                CanonicalVersion = snapshot.Version;
                CompletedTicks = snapshot.CompletedTicks;
                RemainingHits = snapshot.RemainingTicks;
                Elapsed = 0f;
            }

            private static StatusInstance CreateInstance(
                StatusApplication application,
                StatusInstanceId instanceId,
                double startTime,
                uint version,
                int completedTicks)
            {
                return new StatusInstance(
                    instanceId,
                    application.Definition,
                    application.SourcePlayerId,
                    application.SourceEntityId,
                    application.TargetEntityId,
                    application.Stack,
                    startTime,
                    application.Duration,
                    application.ExecutionAuthority,
                    version,
                    application.TickDamage,
                    application.NumberOfHits,
                    completedTicks,
                    application.HitIntervalDuration,
                    application.Priority,
                    application.DamageSourceId,
                    application.SourceContext);
            }
        }
    }
}
