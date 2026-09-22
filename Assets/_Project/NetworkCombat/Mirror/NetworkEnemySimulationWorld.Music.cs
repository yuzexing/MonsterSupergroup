using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.AI.Enemy;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkEnemySimulationWorld
    {
        // Admission and root-event deduplication occur in NetworkCombatWorld before routing.
        [Server]
        internal MusicEffectResult ServerApplyMusicKnockback(uint player, ulong eventId, Vector2 origin,
            float radius, float distance, float duration)
        {
            var combat = NetworkCombatWorld.Instance;
            if (combat == null || distance <= 0f || duration <= 0f) return MusicEffectResult.Empty;
            var curve = AnimationCurve.Linear(0, 0, 1, 1);
            var preset = new EnemyKnockbackSettings
            {
                Distance = distance, SpeedMultiplier = 1f / duration, StaggerTime = 0,
                FixedDirection = false, Direction = Vector2.zero,
                PreWrapMode = (int)WrapMode.ClampForever, PostWrapMode = (int)WrapMode.ClampForever,
                CurveKeys = new[] { EnemyKnockbackCurveKey.From(curve.keys[0]), EnemyKnockbackCurveKey.From(curve.keys[1]) }
            };
            var positions = new List<Vector2>();
            foreach (var enemy in enemies.Values)
            {
                if (enemy == null || !enemy.IsCanonicalAlive ||
                    !combat.Gateway.Ledger.TryGetState(enemy.netId, out var canonical) || !canonical.Alive ||
                    canonical.AbsoluteInvulnerable ||
                    !enemy.TryGetComponent<EnemyController>(out var controller) || controller.IsImmune ||
                    controller.stats.KnockBackMultiplier <= 0 ||
                    !Registry.TryGetAssignment(enemy.netId, out var assignment) ||
                    assignment.Host == EnemySimulationHost.Frozen) continue;
                Vector2 position = Registry.TryGetLatestSnapshot(enemy.netId, out var snapshot)
                    ? snapshot.Position : (Vector2)enemy.transform.position;
                if (snapshot.Runtime.Knockback.Active || enemy.HasActiveNetworkKnockback) continue;
                if ((position - origin).sqrMagnitude > radius * radius) continue;
                if (knockbackCommandId == ulong.MaxValue)
                    throw new InvalidOperationException("Knockback command sequence exhausted.");
                var command = new EnemyKnockbackCommand
                {
                    Kind = EnemyKnockbackKind.Music, EnemyEntityId = enemy.netId,
                    AssignmentEpoch = assignment.Epoch, SourcePlayerId = player,
                    AbilityCombatId = ServerCombatGateway.MusicCombatId, RootEventId = eventId,
                    CommandId = ++knockbackCommandId, IssuedAt = EnemySimulationClock.Now,
                    InterruptedActionId = controller.attackScript?.SupportsSharedTimeline == true
                        ? snapshot.Runtime.Action.ActionId : 0,
                    Origin = origin, MultiplierSum = 0f, Settings = preset
                };
                RouteKnockback(enemy, command);
                positions.Add(position);
            }
            return new MusicEffectResult { Positions = positions.ToArray(), AffectedCount = positions.Count };
        }
    }
}
