using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.AI.Enemy;
using Mirror;
using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkCombatWorld
    {
        private const double PendingEnemyHitLifetime = 0.5;
        private readonly Dictionary<uint, EnemyHitHistory> enemyHitHistory =
            new Dictionary<uint, EnemyHitHistory>();
        private readonly Dictionary<uint, PendingEnemyHit> pendingEnemyHits =
            new Dictionary<uint, PendingEnemyHit>();
        private readonly List<uint> pendingEnemyHitIds = new List<uint>();

        public event Action<EnemyHitPresentation> EnemyHitPresented;

        public void PresentPredictedEnemyHit(CombatEvent damage)
        {
            if (damage.Kind != CombatEventKind.DamageResolved ||
                damage.PredictedAppliedDamage.Value <= 0) return;
            PresentEnemyHit(new EnemyHitPresentation
            {
                DamageEventId = damage.Context.EventId.Value,
                TargetEntityId = damage.Context.TargetEntityId,
                Damage = damage.ResolvedDamage.Value,
                PresentationDamageType = (byte)damage.PresentationDamageType,
                IsCritical = damage.ResolvedDamage.IsCritical,
                SourcePlayerId = damage.Context.SourcePlayerId,
                DamageSourceId = damage.ResolvedDamage.Id
            }, false);
        }

        private void PresentConfirmedEnemyHits(EnemyHitPresentation[] hits)
        {
            if (hits == null) return;
            foreach (var hit in hits) PresentEnemyHit(hit, true);
        }

        private void PresentEnemyHit(EnemyHitPresentation hit, bool confirmed)
        {
            PresentEnemyDamageNumber(hit, confirmed);
            if (!ClientStarted || hit.DamageEventId == 0 ||
                !TryResolveEnemy(hit.TargetEntityId, out var agent, out var enemy)) return;

            if (!enemyHitHistory.TryGetValue(hit.TargetEntityId, out var history))
            {
                history = new EnemyHitHistory();
                enemyHitHistory.Add(hit.TargetEntityId, history);
            }
            if (confirmed)
            {
                if (hit.TargetStateVersion == 0 || hit.TargetStateVersion <= history.ConfirmedVersion ||
                    (Replica.TryGetEntity(hit.TargetEntityId, out var state) &&
                     state.StateVersion > hit.TargetStateVersion)) return;
                history.ConfirmedVersion = hit.TargetStateVersion;
            }

            double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            if (history.Played.IsProcessed(hit.DamageEventId, now)) return;
            if (!agent.ProductEnemyInitialized)
            {
                // An echo must not prolong the same pending hit's lifetime.
                if (!pendingEnemyHits.TryGetValue(hit.TargetEntityId, out var pending) ||
                    pending.Hit.DamageEventId != hit.DamageEventId)
                    pendingEnemyHits[hit.TargetEntityId] = new PendingEnemyHit(hit, now + PendingEnemyHitLifetime);
                else if (confirmed)
                    pendingEnemyHits[hit.TargetEntityId] = new PendingEnemyHit(hit, pending.ExpiresAt);
                return;
            }

            pendingEnemyHits.Remove(hit.TargetEntityId);
            TryPlayEnemyHit(enemy, hit, history, now);
        }

        public void TryPresentPendingEnemyHit(uint entityId)
        {
            if (!pendingEnemyHits.TryGetValue(entityId, out var pending)) return;
            double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            if (!ClientStarted || now >= pending.ExpiresAt ||
                !TryResolveEnemy(entityId, out var agent, out var enemy) ||
                (pending.Hit.TargetStateVersion != 0 && Replica.TryGetEntity(entityId, out var state) &&
                 state.StateVersion > pending.Hit.TargetStateVersion))
            {
                pendingEnemyHits.Remove(entityId);
                return;
            }
            if (!agent.ProductEnemyInitialized) return;
            pendingEnemyHits.Remove(entityId);
            if (enemyHitHistory.TryGetValue(entityId, out var history) &&
                !history.Played.IsProcessed(pending.Hit.DamageEventId, now))
                TryPlayEnemyHit(enemy, pending.Hit, history, now);
        }

        private void TryPlayEnemyHit(EnemyController enemy, EnemyHitPresentation hit,
            EnemyHitHistory history, double now)
        {
            if (enemy.enemyAnimator == null || !enemy.enemyAnimator.TryHurtBlinkAnimation()) return;
            history.Played.MarkProcessed(hit.DamageEventId, now);
            EnemyHitPresented?.Invoke(hit);
        }

        private static bool TryResolveEnemy(uint entityId,
            out NetworkEnemySimulationAgent agent, out EnemyController enemy)
        {
            agent = null;
            enemy = null;
            if (entityId == 0 || !NetworkClient.spawned.TryGetValue(entityId, out var identity) ||
                identity == null) return false;
            agent = identity.GetComponent<NetworkEnemySimulationAgent>();
            enemy = identity.GetComponent<EnemyController>();
            return agent != null && enemy != null;
        }

        private void LateUpdate()
        {
            if (pendingEnemyHits.Count == 0) return;
            pendingEnemyHitIds.Clear();
            pendingEnemyHitIds.AddRange(pendingEnemyHits.Keys);
            foreach (uint id in pendingEnemyHitIds) TryPresentPendingEnemyHit(id);
        }

        public void ForgetEnemyHitPresentation(uint entityId)
        {
            pendingEnemyHits.Remove(entityId);
            enemyHitHistory.Remove(entityId);
        }

        private void ClearEnemyHitPresentations()
        {
            ClearEnemyDamageNumbers();
            enemyHitHistory.Clear();
            pendingEnemyHits.Clear();
            pendingEnemyHitIds.Clear();
        }

        private sealed class EnemyHitHistory
        {
            public readonly ProcessedEventCache Played = new ProcessedEventCache(capacity: 4096);
            public uint ConfirmedVersion;
        }

        private readonly struct PendingEnemyHit
        {
            public readonly EnemyHitPresentation Hit;
            public readonly double ExpiresAt;
            public PendingEnemyHit(EnemyHitPresentation hit, double expiresAt)
            {
                Hit = hit;
                ExpiresAt = expiresAt;
            }
        }
    }
}
