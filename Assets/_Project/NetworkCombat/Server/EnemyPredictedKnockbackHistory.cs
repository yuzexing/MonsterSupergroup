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

        public bool TryRemember(ulong id, double now)
        {
            pending.RemoveAll(value => value.ExpiresAt <= now);
            if (id == 0 || pending.Count >= Capacity || pending.Exists(value => value.DamageEventId == id)) return false;
            pending.Add(new EnemyPredictedKnockbackReceipt { DamageEventId = id, ExpiresAt = now + 3 });
            return true;
        }
        public void Acknowledge(ulong id) => pending.RemoveAll(value => value.DamageEventId == id);
        public EnemyPredictedKnockbackReceipt[] Capture(double now) => pending.Count == 0
            ? Array.Empty<EnemyPredictedKnockbackReceipt>() : pending.FindAll(value => value.ExpiresAt > now).ToArray();
        public void Restore(EnemyPredictedKnockbackReceipt[] receipts, double now)
        {
            pending.RemoveAll(value => value.ExpiresAt <= now);
            if (receipts == null) return;
            foreach (var value in receipts)
                if (value.ExpiresAt > now && pending.Count < Capacity && !pending.Exists(item => item.DamageEventId == value.DamageEventId))
                    pending.Add(value);
        }
        public void Clear() => pending.Clear();
    }
}
