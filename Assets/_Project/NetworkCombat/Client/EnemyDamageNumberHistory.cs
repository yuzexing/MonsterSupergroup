using System.Collections.Generic;

namespace MonsterSupergroup.NetworkCombat
{
    // Kept across despawn/authority handoff so a queued Host death cannot replay a prediction.
    // Both caches are bounded and reset at the world/session boundary.
    public sealed class EnemyDamageNumberHistory
    {
        private const int Capacity = 4096;
        private const double Retention = 120;
        private readonly ProcessedEventCache played = new ProcessedEventCache();
        private readonly Dictionary<uint, Confirmation> confirmations = new Dictionary<uint, Confirmation>();
        private readonly Queue<Confirmation> expiry = new Queue<Confirmation>();

        public bool CanPresent(EnemyHitPresentation hit, bool confirmed, uint replicaVersion, double now)
        {
            Prune(now);
            if (hit.DamageEventId == 0 || hit.TargetEntityId == 0 || hit.Damage <= 0) return false;
            if (confirmed)
            {
                if (hit.TargetStateVersion == 0 || hit.TargetStateVersion < replicaVersion) return false;
                if (confirmations.TryGetValue(hit.TargetEntityId, out var previous) &&
                    (hit.TargetStateVersion < previous.Version ||
                     (hit.TargetStateVersion == previous.Version && hit.DamageEventId != previous.EventId))) return false;
                if (previous.Version != hit.TargetStateVersion)
                {
                    var receipt = new Confirmation(hit, now + Retention);
                    confirmations[hit.TargetEntityId] = receipt;
                    expiry.Enqueue(receipt);
                    while (expiry.Count > Capacity) RemoveOldest();
                }
            }
            return !played.IsProcessed(hit.DamageEventId, now);
        }

        public void MarkPresented(ulong eventId, double now) => played.MarkProcessed(eventId, now);

        public void Clear()
        {
            played.Clear();
            confirmations.Clear();
            expiry.Clear();
        }

        private void Prune(double now)
        {
            while (expiry.Count > 0 && expiry.Peek().ExpiresAt <= now) RemoveOldest();
        }

        private void RemoveOldest()
        {
            var old = expiry.Dequeue();
            if (confirmations.TryGetValue(old.Target, out var current) && current.EventId == old.EventId)
                confirmations.Remove(old.Target);
        }

        private readonly struct Confirmation
        {
            public readonly uint Target, Version;
            public readonly ulong EventId;
            public readonly double ExpiresAt;
            public Confirmation(EnemyHitPresentation hit, double expiresAt)
            {
                Target = hit.TargetEntityId;
                Version = hit.TargetStateVersion;
                EventId = hit.DamageEventId;
                ExpiresAt = expiresAt;
            }
        }
    }
}
