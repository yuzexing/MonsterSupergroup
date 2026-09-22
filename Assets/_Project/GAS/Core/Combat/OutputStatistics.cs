using System;

namespace MonsterSupergroup.GAS
{
    [Serializable] public struct OutputStatisticsState
    {
        public float totalDamage, criticalDamage;
    }
    [Serializable] public struct OutputStatisticInput
    {
        public uint weaponId;
        public float value;
        public bool critical;
        public string metric;
    }
    public interface IOutputStatisticsEvidence { OutputStatisticsState CaptureOutputStatistics(); }
    /// <summary>Shared with the presentation counter. This deliberately does not deduplicate or clamp damage.</summary>
    public static class OutputStatistics
    {
        public static OutputStatisticsState ApplyDamage(OutputStatisticsState state, OutputStatisticInput input)
        {
            state.totalDamage += input.value;
            if (input.critical) state.criticalDamage += input.value;
            return state;
        }
    }
    public static class CombatOutputEvidence
    {
        [ThreadStatic] private static CombatContext current;
        public static CombatContext Current => current;
        public static ContextScope Enter(CombatContext context) => new ContextScope(context);
        public struct ContextScope : IDisposable
        {
            private readonly CombatContext previous;
            internal ContextScope(CombatContext context) { previous = current; current = context; }
            public void Dispose() { current = previous; }
        }
        public static string Register(IOutputStatisticsEvidence entry) => !CombatEvidence.Enabled ? null :
            CombatEvidence.Register(entry, "output_stats", value => ((IOutputStatisticsEvidence)value).CaptureOutputStatistics());
        public static void Record(string engine, OutputStatisticInput input, OutputStatisticsState before, OutputStatisticsState after)
        {
            if (!CombatEvidence.Enabled) return;
            CombatEvidence.Write(new DiagnosticRecord { role = "Owner", stage = "stats.damage", operation = "ApplyDamage", engine = engine,
                eventId = current.IsValid ? current.EventId.Value.ToString() : null,
                rootEventId = current.IsValid ? current.RootEventId.Value.ToString() : null,
                parentEventId = current.ParentEventId.IsValid ? current.ParentEventId.Value.ToString() : null,
                source = current.SourcePlayerId, target = current.TargetEntityId,
                input = input, before = before, after = after, outcome = "Applied",
                reason = current.IsValid ? "ComputedDamageCounter" : "MissingDamageEventContext", critical = true, estimatedBytes = 768 });
        }
    }
}
