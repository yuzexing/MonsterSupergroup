using System.Collections.Generic;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed class EnemyProjectileHistory
    {
        private readonly int capacity;
        private readonly double retention;
        private readonly HashSet<EnemyProjectileKey> keys = new HashSet<EnemyProjectileKey>();
        private readonly Queue<(EnemyProjectileKey key, double expires)> order = new Queue<(EnemyProjectileKey, double)>();
        public EnemyProjectileHistory(int capacity = 262144, double retention = 120) { this.capacity = capacity; this.retention = retention; }
        public bool Contains(EnemyProjectileKey key, double now) { Prune(now); return keys.Contains(key); }
        public bool Add(EnemyProjectileKey key, double now)
        {
            Prune(now);
            if (!keys.Add(key)) return false;
            order.Enqueue((key, now + retention));
            while (keys.Count > capacity) keys.Remove(order.Dequeue().key);
            return true;
        }
        private void Prune(double now) { while (order.Count > 0 && order.Peek().expires <= now) keys.Remove(order.Dequeue().key); }
        public void Clear() { keys.Clear(); order.Clear(); }
    }
}
