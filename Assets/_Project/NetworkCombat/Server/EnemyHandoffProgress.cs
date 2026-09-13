namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>One in-flight assignment and one replaceable intent, never a request queue.</summary>
    public sealed class EnemyHandoffProgress
    {
        public uint PendingTarget { get; private set; }
        public EnemyTargetChangeReason PendingReason { get; private set; }
        public bool HasPending { get; private set; }
        public uint AwaitingEpoch { get; private set; }
        public EnemyHandoffDiagnostics Diagnostics;

        public bool Request(uint target, uint current, EnemyTargetChangeReason reason)
        {
            Diagnostics.Requests++;
            if (target == current && HasPending)
            {
                HasPending = false; PendingTarget = 0;
                Diagnostics.RequestedTarget = current; Diagnostics.Coalesced++;
                return true;
            }
            if ((HasPending && PendingTarget == target) || (!HasPending && current == target)) return false;
            if (HasPending) Diagnostics.Coalesced++;
            PendingTarget = target; PendingReason = reason; HasPending = true;
            Diagnostics.RequestedTarget = target;
            return true;
        }
        public void Begin(uint epoch, uint target, bool client, double now)
        {
            HasPending = false;
            PendingTarget = 0;
            AwaitingEpoch = client ? epoch : 0;
            Diagnostics.RequestedTarget = target;
            Diagnostics.AwaitingFirstSnapshot = client;
            Diagnostics.StartedAt = now;
        }
        public void Observe(uint epoch, double now)
        {
            if (Diagnostics.LastSnapshotAt > 0)
                Diagnostics.MaximumSnapshotGap = System.Math.Max(Diagnostics.MaximumSnapshotGap, now - Diagnostics.LastSnapshotAt);
            Diagnostics.LastSnapshotAt = now;
            if (AwaitingEpoch == 0 || AwaitingEpoch != epoch) return;
            AwaitingEpoch = 0; Diagnostics.AwaitingFirstSnapshot = false;
            Diagnostics.LastDuration = now - Diagnostics.StartedAt; Diagnostics.Completed++;
        }
        public bool TimedOut(double now) => AwaitingEpoch != 0 && now - Diagnostics.StartedAt >= 1;
    }
}
