using System;
using System.Collections.Generic;
using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>
    /// Converts owner-client GAS output into transport-neutral submission contracts.
    /// It never performs RPCs and never emits ConfirmedKill.
    /// </summary>
    public sealed class ClientCombatCollector : ICombatEventSink, IDisposable
    {
        private readonly uint localPlayerId;
        private readonly ICombatEventIdSource eventIds;
        private readonly int capacity;
        private readonly CombatTraceRecorder trace;
        private readonly List<CombatResult> results = new List<CombatResult>();
        private readonly List<StatusMutation> statusMutations = new List<StatusMutation>();
        private readonly List<PlayerHealthReport> playerHealthReports =
            new List<PlayerHealthReport>();
        private readonly Dictionary<StatusInstanceId, int> lastSubmittedStacks =
            new Dictionary<StatusInstanceId, int>();
        private readonly HashSet<StatusController> observedStatuses =
            new HashSet<StatusController>();

        public ClientCombatCollector(
            uint localPlayerId,
            ICombatEventIdSource eventIds,
            int capacity = 4096,
            CombatTraceRecorder trace = null)
        {
            if (localPlayerId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(localPlayerId));
            }

            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            this.localPlayerId = localPlayerId;
            this.eventIds = eventIds ?? throw new ArgumentNullException(nameof(eventIds));
            this.capacity = capacity;
            this.trace = trace;
        }

        public int PendingResultCount => results.Count;
        public int PendingStatusMutationCount => statusMutations.Count;
        public int PendingPlayerHealthReportCount => playerHealthReports.Count;
        public bool RequiresFlush =>
            results.Count + statusMutations.Count + playerHealthReports.Count >= capacity;

        public void Observe(StatusController controller)
        {
            if (controller == null)
            {
                throw new ArgumentNullException(nameof(controller));
            }

            if (observedStatuses.Add(controller))
            {
                controller.Changed += HandleStatusChanged;
            }
        }

        public void StopObserving(StatusController controller)
        {
            if (controller != null && observedStatuses.Remove(controller))
            {
                controller.Changed -= HandleStatusChanged;
            }
        }

        public void Publish(CombatEvent combatEvent)
        {
            trace?.Publish(combatEvent);
            if (combatEvent.Kind != CombatEventKind.DamageResolved ||
                combatEvent.ResolvedDamage.Value <= 0 ||
                combatEvent.Context.SourcePlayerId != localPlayerId)
            {
                return;
            }

            EnsureCapacity();
            results.Add(CombatResult.From(combatEvent));
        }

        public void EnqueuePlayerHealth(PlayerHealthReport report)
        {
            if (report.PlayerId != localPlayerId)
            {
                throw new InvalidOperationException(
                    "A collector may only report its owner's final player state.");
            }

            EnsureCapacity();
            playerHealthReports.Add(report);
        }

        public CombatSubmissionBatch Drain(
            uint batchSequence,
            int maxResults = 256,
            int maxStatusMutations = 128,
            int maxPlayerReports = 8)
        {
            if (batchSequence == 0u)
            {
                throw new ArgumentOutOfRangeException(nameof(batchSequence));
            }

            return new CombatSubmissionBatch
            {
                BatchSequence = batchSequence,
                Results = Take(results, maxResults),
                StatusMutations = Take(statusMutations, maxStatusMutations),
                PlayerHealthReports = Take(playerHealthReports, maxPlayerReports)
            };
        }

        public void Dispose()
        {
            foreach (StatusController controller in observedStatuses)
            {
                controller.Changed -= HandleStatusChanged;
            }

            observedStatuses.Clear();
            results.Clear();
            statusMutations.Clear();
            playerHealthReports.Clear();
            lastSubmittedStacks.Clear();
        }

        private void HandleStatusChanged(StatusChange change)
        {
            StatusInstance instance = change.Instance;
            if (change.Origin != StatusStateOrigin.Predicted ||
                instance.ExecutionAuthority != StatusExecutionAuthority.SourceClient ||
                instance.SourcePlayerId != localPlayerId)
            {
                return;
            }

            EnsureCapacity();
            lastSubmittedStacks.TryGetValue(instance.InstanceId, out int previousStack);
            int nextStack = change.Kind == StatusChangeKind.Removed ? 0 : instance.Stack;
            int delta = nextStack - previousStack;
            CombatEventId mutationEventId = eventIds.Next();
            trace?.RecordStatus(change, mutationEventId);
            statusMutations.Add(StatusMutation.From(change, mutationEventId, delta));

            if (nextStack == 0)
            {
                lastSubmittedStacks.Remove(instance.InstanceId);
            }
            else
            {
                lastSubmittedStacks[instance.InstanceId] = nextStack;
            }
        }

        private void EnsureCapacity()
        {
            if (results.Count + statusMutations.Count + playerHealthReports.Count >= capacity)
            {
                throw new InvalidOperationException(
                    "Client combat submission buffer is full. Flush it before simulating more shared results.");
            }
        }

        private static T[] Take<T>(List<T> source, int maximum)
        {
            if (maximum < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maximum));
            }

            int count = Math.Min(maximum, source.Count);
            if (count == 0)
            {
                return Array.Empty<T>();
            }

            T[] result = source.GetRange(0, count).ToArray();
            source.RemoveRange(0, count);
            return result;
        }
    }
}
