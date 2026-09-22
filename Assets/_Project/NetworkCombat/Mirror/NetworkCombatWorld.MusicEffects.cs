using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.AI.Enemy;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public struct MusicEffectResult
    {
        public Vector2[] Positions;
        public int AffectedCount;
        public static MusicEffectResult Empty => new MusicEffectResult { Positions = Array.Empty<Vector2>() };
    }

    public sealed partial class NetworkCombatWorld
    {
        [Server]
        public MusicEffectResult ServerApplyMusicEffect(uint player, uint source, ulong eventId,
            MusicEffect effect, MusicParameters p)
        {
            if (!PrototypesEnabled || !p.IsValid || !NetworkServer.spawned.TryGetValue(player, out var identity) ||
                identity == null) return MusicEffectResult.Empty;
            Vector2 origin = identity.transform.position;
            double now = NetworkTime.time;
            if (effect == MusicEffect.Speed)
            {
                // The per-player music component replicates the independent speed deadline.
                return Gateway.TryAdmitMusicEffect(player, source, eventId, now)
                    ? new MusicEffectResult { Positions = new[] { origin }, AffectedCount = 1 }
                    : MusicEffectResult.Empty;
            }
            if (effect == MusicEffect.Push)
            {
                if (!Gateway.TryAdmitMusicEffect(player, source, eventId, now)) return MusicEffectResult.Empty;
                return NetworkEnemySimulationWorld.Instance != null
                    ? NetworkEnemySimulationWorld.Instance.ServerApplyMusicKnockback(player, eventId,
                        origin, p.PushRadius, p.PushDistance, p.PushDuration)
                    : MusicEffectResult.Empty;
            }
            if (effect != MusicEffect.Lightning && effect != MusicEffect.Finale) return MusicEffectResult.Empty;
            float radius = effect == MusicEffect.Finale ? p.FinaleRadius : p.LightningRadius;
            int damage = Mathf.RoundToInt(effect == MusicEffect.Finale ? p.FinaleDamage : p.LightningDamage);
            var candidates = new List<uint>();
            var positions = new Dictionary<uint, Vector2>();
            foreach (var enemyIdentity in NetworkServer.spawned.Values)
            {
                if (enemyIdentity == null || !enemyIdentity.TryGetComponent<EnemyController>(out var enemy) ||
                    !enemy.isActiveAndEnabled || enemy.IsImmune ||
                    !Gateway.Ledger.TryGetState(enemyIdentity.netId, out var state) ||
                    state.Kind != (byte)CombatEntityKind.Enemy || !state.Alive || state.AbsoluteInvulnerable) continue;
                Vector2 position = enemyIdentity.transform.position;
                var simulations = NetworkEnemySimulationWorld.Instance;
                if (simulations != null && simulations.Registry.TryGetLatestSnapshot(enemyIdentity.netId, out var snapshot))
                    position = snapshot.Position;
                if ((position - origin).sqrMagnitude > radius * radius) continue;
                candidates.Add(enemyIdentity.netId);
                positions.Add(enemyIdentity.netId, position);
            }
            if (effect == MusicEffect.Lightning)
            {
                int count = Mathf.Min(candidates.Count, p.LightningTargets);
                // Partial Fisher-Yates: every eligible enemy has equal probability, without replacement.
                for (int index = 0; index < count; index++)
                {
                    int selected = UnityEngine.Random.Range(index, candidates.Count);
                    (candidates[index], candidates[selected]) = (candidates[selected], candidates[index]);
                }
                if (candidates.Count > count) candidates.RemoveRange(count, candidates.Count - count);
            }
            var applied = Gateway.ProcessMusicDamage(player, source, candidates.ToArray(), damage, eventId, now, out var batch);
            if (batch.ServerSequence != 0) Broadcast(batch);
            var affectedPositions = new Vector2[applied.Length];
            for (int index = 0; index < applied.Length; index++)
                affectedPositions[index] = positions[applied[index].State.EntityId];
            return new MusicEffectResult { Positions = affectedPositions, AffectedCount = applied.Length };
        }
    }
}
