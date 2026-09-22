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
        private readonly Func<double> timeSource;
        private readonly Func<uint, bool> isClientFinalEnemy;
        private readonly Dictionary<ulong, PendingDeath> deaths = new Dictionary<ulong, PendingDeath>();
        public const int MaximumDeathRetriesPerSecond = 128;
        private int deathRetriesInWindow;
        private double nextDeathRetryWindow;
        private readonly List<CombatResult> results = new List<CombatResult>();
        private readonly HashSet<ulong> queuedDamage = new HashSet<ulong>();
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
            CombatTraceRecorder trace = null, Func<double> timeSource = null, Func<uint, bool> isClientFinalEnemy = null)
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
            this.timeSource = timeSource ?? (() => 0d);
            this.isClientFinalEnemy = isClientFinalEnemy ?? (_ => true);
        }

        public int PendingResultCount => results.Count;
        public event Action<CombatEvent> DamageResolved;
        public int PendingStatusMutationCount => statusMutations.Count;
        public int PendingPlayerHealthReportCount => playerHealthReports.Count;
        public int PendingEnemyDeathCount => deaths.Count;
        public long DeathReceiptsReceived { get; private set; }
        public double LastDeathConfirmationSeconds { get; private set; }
        public double MaximumDeathConfirmationSeconds { get; private set; }
        public bool HasDueDeaths(double now)
        {
            foreach (var pending in deaths.Values)
                if (pending.NextSend <= now && (!pending.Sent || CanRetryDeath(now)) &&
                    !queuedDamage.Contains(pending.Report.CauseEventId)) return true;
            return false;
        }
        private bool CanRetryDeath(double now) => now >= nextDeathRetryWindow || deathRetriesInWindow < MaximumDeathRetriesPerSecond;
        public double OldestPendingDeathAge(double now)
        {
            double age = 0;
            foreach (var pending in deaths.Values) age = Math.Max(age, now - pending.CreatedAt);
            return age;
        }
        public bool AcknowledgeDeath(EnemyDeathReceipt receipt, double now)
        {
            if (!deaths.TryGetValue(receipt.ReportEventId, out var pending) ||
                pending.Report.TargetEntityId != receipt.TargetEntityId) return false;
            deaths.Remove(receipt.ReportEventId);
            DeathReceiptsReceived++;
            LastDeathConfirmationSeconds = Math.Max(0, now - pending.CreatedAt);
            MaximumDeathConfirmationSeconds = Math.Max(MaximumDeathConfirmationSeconds, LastDeathConfirmationSeconds);
            return true;
        }
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
            if (combatEvent.Kind == CombatEventKind.PredictedLethalHit &&
                combatEvent.Context.SourcePlayerId == localPlayerId && combatEvent.Context.TargetEntityId != 0 &&
                isClientFinalEnemy(combatEvent.Context.TargetEntityId))
            {
                var context = combatEvent.Context;
                if (!deaths.ContainsKey(context.EventId.Value))
                    deaths.Add(context.EventId.Value, new PendingDeath(new EnemyDeathReport
                    {
                        EventId = context.EventId.Value, CauseEventId = context.ParentEventId.Value,
                        Sequence = context.Sequence, SourcePlayerId = localPlayerId,
                        SourceEntityId = context.SourceEntityId, TargetEntityId = context.TargetEntityId
                    }, timeSource()));
                return;
            }
            if (combatEvent.Kind != CombatEventKind.DamageResolved ||
                combatEvent.ResolvedDamage.Value <= 0 ||
                combatEvent.Context.SourcePlayerId != localPlayerId)
            {
                return;
            }

            EnsureCapacity();
            results.Add(CombatResult.From(combatEvent));
            queuedDamage.Add(combatEvent.Context.EventId.Value);
            DamageResolved?.Invoke(combatEvent);
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

        // The gameplay hit notification runs synchronously after DamageResolved, before Drain.
        public bool TryAttachKnockback(ulong damageEventId, uint targetEntityId, OrdinaryHitKnockback knockback)
        {
            if (!knockback.IsValid) return false;
            for (int index = results.Count - 1; index >= 0; index--)
            {
                CombatResult result = results[index];
                if (result.EventId != damageEventId || result.TargetEntityId != targetEntityId) continue;
                if (result.Knockback.Requested) return false;
                result.Knockback = knockback;
                results[index] = result;
                return true;
            }
            return false;
        }

        public CombatSubmissionBatch Drain(
            uint batchSequence,
            int maxResults = 256,
            int maxStatusMutations = 128,
            int maxPlayerReports = 8, double now = 0, int maxDeathReports = 128)
        {
            if (batchSequence == 0u)
            {
                throw new ArgumentOutOfRangeException(nameof(batchSequence));
            }

            var drainedResults = Take(results, maxResults);
            foreach (var result in drainedResults) queuedDamage.Remove(result.EventId);
            return new CombatSubmissionBatch
            {
                BatchSequence = batchSequence,
                Results = drainedResults,
                StatusMutations = Take(statusMutations, maxStatusMutations),
                PlayerHealthReports = Take(playerHealthReports, maxPlayerReports),
                EnemyDeathReports = TakeDeaths(now, maxDeathReports)
            };
        }

        private EnemyDeathReport[] TakeDeaths(double now, int maximum)
        {
            if (maximum < 1) throw new ArgumentOutOfRangeException(nameof(maximum));
            if (!HasDueDeaths(now)) return Array.Empty<EnemyDeathReport>();
            if (now >= nextDeathRetryWindow) { deathRetriesInWindow = 0; nextDeathRetryWindow = now + 1d; }
            var reports = new List<EnemyDeathReport>(Math.Min(maximum, deaths.Count));
            foreach (var pending in deaths.Values)
            {
                if (pending.NextSend > now || (pending.Sent && !CanRetryDeath(now)) ||
                    queuedDamage.Contains(pending.Report.CauseEventId)) continue;
                reports.Add(pending.Report);
                if (pending.Sent) deathRetriesInWindow++;
                pending.Sent = true;
                pending.NextSend = now + 1d;
                if (reports.Count == maximum) break;
            }
            return reports.ToArray();
        }

        private sealed class PendingDeath
        {
            public readonly EnemyDeathReport Report;
            public readonly double CreatedAt;
            public double NextSend;
            public bool Sent;
            public PendingDeath(EnemyDeathReport report, double now)
            { Report = report; CreatedAt = now; NextSend = now; }
        }

        public void Dispose()
        {
            foreach (StatusController controller in observedStatuses)
            {
                controller.Changed -= HandleStatusChanged;
            }

            observedStatuses.Clear();
            results.Clear();
            queuedDamage.Clear();
            deaths.Clear();
            statusMutations.Clear();
            playerHealthReports.Clear();
            lastSubmittedStacks.Clear();
            DamageResolved = null;
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

            T[] result = new T[count];
            source.CopyTo(0, result, 0, count);
            source.RemoveRange(0, count);
            return result;
        }
    }
}
