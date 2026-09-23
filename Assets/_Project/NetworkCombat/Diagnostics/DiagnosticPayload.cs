using System;

namespace MonsterSupergroup.NetworkCombat.Diagnostics
{
    [Serializable] public sealed class ReplayOutResult { public object result; public object[] outValues; }
    [Serializable] public sealed class CanonicalReceiveEvidence { public uint incomingRound; public object batch; }
    /// <summary>Detach transport arrays before returning to gameplay; presentation code may amend the returned batch later.</summary>
    public static class DiagnosticPayload
    {
        private static T[] Copy<T>(T[] values) => values == null || values.Length == 0 ? values : (T[])values.Clone();
        public static CanonicalWorldBatch Freeze(CanonicalWorldBatch batch)
        {
            batch.Entities = Copy(batch.Entities); batch.Statuses = Copy(batch.Statuses);
            batch.ConfirmedKills = Copy(batch.ConfirmedKills); batch.EnemyHitPresentations = Copy(batch.EnemyHitPresentations); return batch;
        }
        public static CombatSubmissionBatch Freeze(CombatSubmissionBatch batch)
        {
            batch.Results = Copy(batch.Results); batch.StatusMutations = Copy(batch.StatusMutations);
            batch.EnemyDeathReports = Copy(batch.EnemyDeathReports); batch.PlayerHealthReports = Copy(batch.PlayerHealthReports); return batch;
        }
        public static EnemyKnockbackSettings Freeze(EnemyKnockbackSettings value) { value.CurveKeys = Copy(value.CurveKeys); return value; }
        public static EnemySimulationSnapshot Freeze(EnemySimulationSnapshot value)
        {
            value.Runtime.KnockbackSettings = Freeze(value.Runtime.KnockbackSettings);
            value.Runtime.PredictedKnockbacks = Copy(value.Runtime.PredictedKnockbacks); return value;
        }
        public static EnemySimulationCheckpoint Freeze(EnemySimulationCheckpoint value) { value.Movement = Freeze(value.Movement); return value; }
        public static EnemyAttackPresentationEdge Freeze(EnemyAttackPresentationEdge value) { value.Checkpoint = Freeze(value.Checkpoint); return value; }
        public static object Freeze(object value)
        {
            switch (value)
            {
                case SharedEvidencePayload shared: return shared; // Queue ownership is acquired explicitly with AcquireLease.
                case CanonicalWorldBatch batch: return Freeze(batch);
                case CombatSubmissionBatch batch: return Freeze(batch);
                case CanonicalReceiveEvidence received: return new CanonicalReceiveEvidence { incomingRound = received.incomingRound, batch = Freeze(received.batch) };
                case ReplayOutResult output: return new ReplayOutResult { result = Freeze(output.result), outValues = (object[])Freeze(output.outValues) };
                case GatewayReplayOutput result: result.batch = Freeze(result.batch); result.receipts = Copy(result.receipts); return result;
                case EnemySimulationSnapshot valueSnapshot: return Freeze(valueSnapshot);
                case EnemySimulationCheckpoint checkpoint: return Freeze(checkpoint);
                case EnemyAttackPresentationEdge edge: return Freeze(edge);
                case EnemySimulationHandoff handoff: handoff.Checkpoint = Freeze(handoff.Checkpoint); return handoff;
                case EnemyKnockbackSettings settings: return Freeze(settings);
                case EnemySimulationSnapshotBatch batch:
                    batch.Snapshots = Copy(batch.Snapshots);
                    if (batch.Snapshots != null) for (int i = 0; i < batch.Snapshots.Length; i++) batch.Snapshots[i] = Freeze(batch.Snapshots[i]); return batch;
                case ReplayCheckpoint checkpoint: checkpoint.state = Freeze(checkpoint.state); return checkpoint;
                case ReplayCheckpointSet set: foreach (var item in set.engines) item.state = Freeze(item.state); return set;
                case object[] arguments:
                    var copy = new object[arguments.Length]; for (int i = 0; i < copy.Length; i++) copy[i] = Freeze(arguments[i]); return copy;
                case Array array: return array.Clone();
                default: return value; // Immutable values, strings, and freshly captured replay DTOs.
            }
        }
    }
}
