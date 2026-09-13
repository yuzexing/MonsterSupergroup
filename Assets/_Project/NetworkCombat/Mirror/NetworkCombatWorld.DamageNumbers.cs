using System;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkCombatWorld
    {
        private readonly EnemyDamageNumberHistory damageNumberHistory = new EnemyDamageNumberHistory();
        public event Action<EnemyHitPresentation> EnemyDamageNumberPresented;

        private void PresentEnemyDamageNumber(EnemyHitPresentation hit, bool confirmed)
        {
            if (!ClientStarted) return;
            double now = Time.realtimeSinceStartupAsDouble;
            uint version = Replica.TryGetEntity(hit.TargetEntityId, out var state) ? state.StateVersion : 0;
            if (!damageNumberHistory.CanPresent(hit, confirmed, version, now)) return;

            Transform anchor = null;
            if (TryResolveEnemy(hit.TargetEntityId, out _, out var enemy))
            {
                anchor = enemy.damagePosition != null ? enemy.damagePosition : enemy.transform;
                if (!hit.HasPosition)
                {
                    hit.Position = DamageNumberPosition(enemy);
                    hit.HasPosition = true;
                }
            }
            // Accepted death edges retain their position even when Mirror already despawned the target.
            if (!hit.HasPosition || PoolManager.Instance == null ||
                !PoolManager.Instance.TrySpawnNetworkDamageNumber(hit.Position, anchor, hit.Damage,
                    (DamageType)hit.PresentationDamageType, hit.IsCritical, hit.SourcePlayerId,
                    hit.DamageSourceId, hit.TargetEntityId)) return;

            damageNumberHistory.MarkPresented(hit.DamageEventId, now);
            EnemyDamageNumberPresented?.Invoke(hit);
        }

        private static Vector3 DamageNumberPosition(EnemyController enemy)
        {
            if (enemy.damagePosition != null) return enemy.damagePosition.position;
            if (enemy.hurtBox != null) return enemy.hurtBox.GetPosition();
            return enemy.transform.position;
        }

        private static void CaptureEnemyHitPositions(EnemyHitPresentation[] hits)
        {
            if (hits == null) return;
            for (int i = 0; i < hits.Length; i++)
            {
                if (!NetworkServer.spawned.TryGetValue(hits[i].TargetEntityId, out var identity) || identity == null) continue;
                var enemy = identity.GetComponent<EnemyController>();
                if (enemy == null) continue;
                hits[i].Position = DamageNumberPosition(enemy);
                hits[i].HasPosition = true;
            }
        }

        private void ClearEnemyDamageNumbers()
        {
            damageNumberHistory.Clear();
            if (PoolManager.Instance != null) PoolManager.Instance.ClearNetworkDamageNumbers();
        }
    }
}
