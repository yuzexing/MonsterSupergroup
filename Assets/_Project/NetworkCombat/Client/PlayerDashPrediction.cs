using System;
using System.Collections.Generic;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>Pending input reservations over the latest server baseline; never a second Build.</summary>
    public sealed class PlayerDashPrediction
    {
        private readonly List<Pending> pending = new List<Pending>();
        private uint lastRevision;
        private uint lastPredictedSequence;
        public PlayerDashRuntime Runtime { get; } = new PlayerDashRuntime();
        public bool HasBaseline => lastRevision != 0;
        public int PendingCount => pending.Count;

        public bool TryPredict(ulong useId, double now, float duration, float recharge)
        {
            var id = new CombatEventId(useId);
            if (!HasBaseline || !id.IsValid || id.Sequence <= lastPredictedSequence || pending.Count >= 64 ||
                !Runtime.TryConsume(now, duration, recharge)) return false;
            pending.Add(new Pending(useId, now, duration, recharge));
            lastPredictedSequence = id.Sequence;
            return true;
        }

        public bool ApplyBaseline(uint revision, ulong acknowledgedUseId, PlayerDashSnapshot snapshot, double now)
        {
            if (revision == 0 || (lastRevision != 0 && unchecked((int)(revision - lastRevision)) <= 0)) return false;
            // Validate the snapshot before discarding pending input, including malformed transport data.
            Runtime.Restore(snapshot, now);
            lastRevision = revision;
            var acknowledged = new CombatEventId(acknowledgedUseId);
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var id = new CombatEventId(pending[i].Id);
                if (acknowledgedUseId != 0 && id.SourceSlot == acknowledged.SourceSlot &&
                    id.ConnectionEpoch == acknowledged.ConnectionEpoch && id.Sequence <= acknowledged.Sequence)
                    pending.RemoveAt(i);
            }
            // An ACK for A can arrive after the Owner has already spent B. Keep B reserved.
            var corrected = Runtime.Capture(now);
            var deadlines = new List<double>(corrected.RechargeReadyAt);
            foreach (Pending input in pending)
            {
                double reservedAt = Math.Max(input.Time, corrected.NextUseAt);
                deadlines.Add(reservedAt + input.Recharge);
                corrected.NextUseAt = reservedAt + input.Duration + PlayerDashRuntime.ChainDelaySeconds;
            }
            // Capacity may have shrunk since prediction. Already spent input must remain reserved
            // even when there are temporarily more outstanding recharges than available slots.
            corrected.RechargeReadyAt = deadlines.ToArray();
            Runtime.Restore(corrected, now);
            return true;
        }

        private readonly struct Pending
        {
            public readonly ulong Id;
            public readonly double Time;
            public readonly float Duration;
            public readonly float Recharge;
            public Pending(ulong id, double time, float duration, float recharge)
            { Id = id; Time = time; Duration = duration; Recharge = recharge; }
        }
    }
}
