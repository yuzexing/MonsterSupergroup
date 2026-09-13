using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.AI.Enemy;
using Mirror;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkEnemySimulationWorld
    {
        private readonly EnemyProjectileHistory endedProjectiles = new EnemyProjectileHistory();
        private readonly EnemyProjectileHistory acceptedTerminations = new EnemyProjectileHistory();
        private readonly Dictionary<EnemyProjectileKey, BulletProjectile> liveProjectiles = new Dictionary<EnemyProjectileKey, BulletProjectile>();
        private readonly List<EnemyProjectileTermination> pendingClientTerminations = new List<EnemyProjectileTermination>();
        private readonly Dictionary<EnemyProjectileKey, (EnemyProjectileTermination value, double expires)> pendingServerTerminations = new Dictionary<EnemyProjectileKey, (EnemyProjectileTermination, double)>();
        public int AcceptedProjectileTerminationCount { get; private set; }
        public event Action<EnemyProjectileTermination> EnemyProjectileTerminated;

        private void EndLocalEnemyProjectile(EnemyProjectileKey key, EnemyProjectileEndReason reason, bool unexpectedDisable = false)
        {
            if (!NetworkClient.active || endedProjectiles.Contains(key, NetworkTime.time)) return;
            var terminal = new EnemyProjectileTermination { Key = key, Reason = reason };
            if (unexpectedDisable && liveProjectiles.TryGetValue(key, out var disabled))
            {
                disabled.OnDisabled = null; disabled.OnReturn = null;
                liveProjectiles.Remove(key); borrowedProjectiles.Remove(disabled);
                Destroy(disabled.gameObject); // Never reparent during OnDisable.
            }
            ApplyEnemyProjectileTermination(terminal);
            pendingClientTerminations.Add(terminal);
        }
        internal void CollectClientProjectileTerminations(List<EnemyProjectileTermination> output)
        { output.Clear(); output.AddRange(pendingClientTerminations); pendingClientTerminations.Clear(); }

        private void SubmitEnemyProjectileTerminations(EnemyProjectileTermination[] values)
        {
            if (values == null) return;
            var expired = new List<EnemyProjectileKey>();
            foreach (var item in pendingServerTerminations) if (item.Value.expires <= NetworkTime.time) expired.Add(item.Key);
            foreach (var key in expired) pendingServerTerminations.Remove(key);
            foreach (var value in values)
            {
                if (!value.IsValid || acceptedTerminations.Contains(value.Key, NetworkTime.time)) continue;
                if (acceptedProjectiles.Contains(value.Key, NetworkTime.time)) ConfirmEnemyProjectileTermination(value);
                else if (pendingServerTerminations.Count < 4096) pendingServerTerminations[value.Key] = (value, NetworkTime.time + 5);
            }
        }
        private void ConfirmEnemyProjectileTermination(EnemyProjectileTermination value)
        {
            if (!acceptedTerminations.Add(value.Key, NetworkTime.time)) return;
            AcceptedProjectileTerminationCount++;
            RpcApplyAttackPresentations(new EnemyAttackPresentationBatch { Round = CurrentRound, ProjectileTerminations = new[] { value } });
        }
        private void AcceptPendingTermination(EnemyProjectileKey key)
        {
            if (pendingServerTerminations.TryGetValue(key, out var pending))
            {
                pendingServerTerminations.Remove(key);
                if (pending.expires > NetworkTime.time) ConfirmEnemyProjectileTermination(pending.value);
            }
        }
        public bool ApplyEnemyProjectileTermination(EnemyProjectileTermination value)
        {
            if (!value.IsValid || !endedProjectiles.Add(value.Key, NetworkTime.time)) return false;
            if (liveProjectiles.TryGetValue(value.Key, out var bullet))
            {
                liveProjectiles.Remove(value.Key);
                ReturnEnemyBullet(bullet);
            }
            EnemyProjectileTerminated?.Invoke(value);
            return true;
        }
    }
}
