namespace MonsterSupergroup.GAS
{
    public sealed partial class StatusController
    {
        private static void RecordTickEvidence(StatusTick tick, bool immediate, string outcome, string reason)
        {
            if (!CombatEvidence.Enabled) return;
            CombatContext context = tick.Instance.SourceContext;
            CombatEvidence.Write(new DiagnosticRecord { role = "StatusExecutor", engine = CombatEvidence.CurrentEngine,
                stage = "status.tick", outcome = outcome, reason = reason,
                rootEventId = context.IsValid ? context.RootEventId.Value.ToString() : null,
                parentEventId = context.IsValid ? context.EventId.Value.ToString() : null,
                source = tick.Instance.SourcePlayerId, target = tick.Instance.TargetEntityId,
                statusInstanceId = tick.InstanceId.Value.ToString(), applicationRevision = tick.Instance.ApplicationRevision,
                stateVersion = tick.Instance.Version, tickIndex = tick.TickIndex,
                input = new { tick, immediate, stack = tick.Instance.Stack, executionAuthority = tick.Instance.ExecutionAuthority },
                critical = true, estimatedBytes = 1024 });
        }
    }
}
