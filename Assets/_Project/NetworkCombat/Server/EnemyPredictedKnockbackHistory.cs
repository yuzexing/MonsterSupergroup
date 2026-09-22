using System;
using System.Collections.Generic;

namespace MonsterSupergroup.NetworkCombat
{
    [Serializable]
    public struct EnemyPredictedKnockbackReceipt
    {
        public ulong DamageEventId;
        public double ExpiresAt;
    }

    /// <summary>Unconfirmed local impulses must remain deduplicated even after their motion has ended.</summary>
    public sealed class EnemyPredictedKnockbackHistory
    {
        public const int Capacity = 256;
        private readonly List<EnemyPredictedKnockbackReceipt> pending = new List<EnemyPredictedKnockbackReceipt>();
        private EnemyPredictedKnockbackReceipt[] cached;

        private void Prune(double now)
        {
            for (int i = pending.Count - 1; i >= 0; i--)
                if (pending[i].ExpiresAt <= now) { pending.RemoveAt(i); cached = null; }
        }
        private bool Contains(ulong id)
        {
            foreach (var value in pending) if (value.DamageEventId == id) return true;
            return false;
        }

        public bool TryRemember(ulong id, double now)
        {
            Prune(now);
            if (id == 0 || pending.Count >= Capacity || Contains(id)) return false;
            pending.Add(new EnemyPredictedKnockbackReceipt { DamageEventId = id, ExpiresAt = now + 3 });
            cached = null;
            return true;
        }
        public void Acknowledge(ulong id)
        {
            for (int i = pending.Count - 1; i >= 0; i--)
                if (pending[i].DamageEventId == id) { pending.RemoveAt(i); cached = null; }
        }
        // Returned snapshots are immutable; a mutation replaces rather than edits the cache.
        public EnemyPredictedKnockbackReceipt[] Capture(double now)
        {
            Prune(now);
            return cached ??= pending.Count == 0 ? Array.Empty<EnemyPredictedKnockbackReceipt>() : pending.ToArray();
        }
        public void Restore(EnemyPredictedKnockbackReceipt[] receipts, double now)
        {
            Prune(now);
            if (receipts == null) return;
            foreach (var value in receipts)
                if (value.ExpiresAt > now && pending.Count < Capacity && !Contains(value.DamageEventId))
                { pending.Add(value); cached = null; }
        }
        public void Clear() { pending.Clear(); cached = null; }
    }
}
