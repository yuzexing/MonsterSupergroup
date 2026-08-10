using System;
using System.Collections.Generic;

namespace MonsterSupergroup.GAS
{
    public sealed class StatusController
    {
        private const float TimeEpsilon = 0.000001f;

        private readonly Action<StatusTick> tickReceiver;
        private readonly Dictionary<EnemyStatusID, List<ActiveStatus>> activeStatuses =
            new Dictionary<EnemyStatusID, List<ActiveStatus>>();

        public StatusController(Action<StatusTick> tickReceiver)
        {
            this.tickReceiver = tickReceiver ?? throw new ArgumentNullException(nameof(tickReceiver));
        }

        public int Count
        {
            get
            {
                int count = 0;
                foreach (List<ActiveStatus> statuses in activeStatuses.Values)
                {
                    count += statuses.Count;
                }

                return count;
            }
        }

        public bool Has(EnemyStatusID statusId)
        {
            return activeStatuses.TryGetValue(statusId, out List<ActiveStatus> statuses) && statuses.Count > 0;
        }

        public int GetStackCount(EnemyStatusID statusId)
        {
            return activeStatuses.TryGetValue(statusId, out List<ActiveStatus> statuses) ? statuses.Count : 0;
        }

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

            if (statuses.Count == 0)
            {
                statuses.Add(new ActiveStatus(application));
                return StatusApplicationResult.Added;
            }

            switch (application.Definition.StackMode)
            {
                case StatusStackMode.Add:
                    if (statuses.Count >= application.Definition.MaxStacks)
                    {
                        return StatusApplicationResult.Rejected;
                    }

                    statuses.Add(new ActiveStatus(application));
                    return StatusApplicationResult.Added;

                case StatusStackMode.Replace:
                    statuses.Clear();
                    statuses.Add(new ActiveStatus(application));
                    return StatusApplicationResult.Replaced;

                case StatusStackMode.HighestPriority:
                    ActiveStatus current = statuses[0];
                    if (application.Priority < current.Application.Priority)
                    {
                        return StatusApplicationResult.Rejected;
                    }

                    statuses[0] = new ActiveStatus(application);
                    return application.Priority > current.Application.Priority
                        ? StatusApplicationResult.Replaced
                        : StatusApplicationResult.Refreshed;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public void Advance(float deltaSeconds)
        {
            if (float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds) || deltaSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds), "Delta time must be finite and non-negative.");
            }

            if (deltaSeconds == 0f || activeStatuses.Count == 0)
            {
                return;
            }

            var pendingTicks = new List<StatusTick>();
            var emptyStatusIds = new List<EnemyStatusID>();

            var statusIds = new List<EnemyStatusID>(activeStatuses.Keys);
            statusIds.Sort();
            for (int statusIdIndex = 0; statusIdIndex < statusIds.Count; statusIdIndex++)
            {
                EnemyStatusID statusId = statusIds[statusIdIndex];
                List<ActiveStatus> statuses = activeStatuses[statusId];
                for (int i = 0; i < statuses.Count; i++)
                {
                    ActiveStatus status = statuses[i];
                    status.Elapsed += deltaSeconds;

                    while (status.RemainingHits > 0 &&
                           status.Elapsed + TimeEpsilon >= status.Application.HitIntervalDuration)
                    {
                        status.Elapsed -= status.Application.HitIntervalDuration;
                        if (status.Elapsed < 0f && status.Elapsed > -TimeEpsilon)
                        {
                            status.Elapsed = 0f;
                        }

                        status.RemainingHits--;
                        int tickIndex = status.Application.NumberOfHits - status.RemainingHits;
                        pendingTicks.Add(new StatusTick(
                            status.Application.Definition.Id,
                            new DamageInfo(status.Application.DamageSourceId, status.Application.TickDamage, false),
                            tickIndex,
                            status.RemainingHits == 0));
                    }

                    if (status.RemainingHits == 0)
                    {
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

            // Dispatch after mutating the collection so receivers may safely apply new statuses.
            for (int i = 0; i < pendingTicks.Count; i++)
            {
                tickReceiver(pendingTicks[i]);
            }
        }

        public bool Consume(EnemyStatusID statusId)
        {
            if (!activeStatuses.TryGetValue(statusId, out List<ActiveStatus> statuses) || statuses.Count == 0)
            {
                return false;
            }

            statuses.RemoveAt(0);
            if (statuses.Count == 0)
            {
                activeStatuses.Remove(statusId);
            }

            return true;
        }

        public bool Clear(EnemyStatusID statusId)
        {
            return activeStatuses.Remove(statusId);
        }

        public void Clear()
        {
            activeStatuses.Clear();
        }

        private static void ValidateCompatibleDefinition(
            IReadOnlyList<ActiveStatus> statuses,
            StatusDefinition definition)
        {
            if (statuses.Count > 0 && !statuses[0].Application.Definition.Equals(definition))
            {
                throw new InvalidOperationException(
                    $"Status {definition.Id} was applied with a definition that differs from its active definition.");
            }
        }

        private sealed class ActiveStatus
        {
            public ActiveStatus(StatusApplication application)
            {
                Application = application;
                RemainingHits = application.NumberOfHits;
            }

            public StatusApplication Application { get; }

            public int RemainingHits { get; set; }

            public float Elapsed { get; set; }
        }
    }
}
