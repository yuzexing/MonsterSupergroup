using System;
using System.Collections.Generic;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkGameplayEnemySpawner
    {
        private readonly Dictionary<uint, double> offscreenSince = new Dictionary<uint, double>();
        private readonly List<NetworkEnemySimulationAgent> repositionCandidates = new List<NetworkEnemySimulationAgent>();
        private readonly List<Bounds> referenceParticipantViews = new List<Bounds>();

        private void UpdateReferenceReposition()
        {
            var reference = settings.Reference;
            double elapsed = schedule.State.Elapsed;
            if (activeParticipants.Count == 0) return;
            referenceParticipantViews.Clear();
            foreach (var participant in activeParticipants) referenceParticipantViews.Add(ReferenceView(participant));
            repositionCandidates.Clear();
            foreach (var identity in NetworkServer.spawned.Values)
                if (identity != null && identity.gameObject.scene == gameObject.scene &&
                    identity.TryGetComponent<NetworkEnemySimulationAgent>(out var enemy) && enemy.Birth.Enabled &&
                    enemy.IsCanonicalAlive && elapsed - enemy.Birth.BornAt >= reference.RepositionGrace)
                    repositionCandidates.Add(enemy);
            foreach (var enemy in repositionCandidates)
            {
                var target = activeParticipants.Find(p => p.AvatarId == enemy.Assignment.AggroTargetPlayerId) ?? activeParticipants[0];
                Vector2 position = enemy.transform.position;
                float outside = GameplayCameraGeometry.MinimumOutsideDistance(position, referenceParticipantViews);
                if (outside == 0) { offscreenSince.Remove(enemy.netId); continue; }
                if (!offscreenSince.TryGetValue(enemy.netId, out double since)) offscreenSince[enemy.netId] = since = elapsed;
                if (!reference.OffscreenProcessingDue(enemy.Birth.BornAt, elapsed, since, outside)) continue;
                var birth = enemy.Birth;
                if (birth.ExpiresAt > 0 && elapsed > birth.ExpiresAt)
                {
                    referenceTrace?.WriteLine(FormattableString.Invariant($"{elapsed:R},retired,{birth.ClipIndex},{birth.SourceEnemy},{birth.Variant},,,{enemy.netId},NoXp,,,,,,,,"));
                    referenceObserved.Remove(enemy.netId); offscreenSince.Remove(enemy.netId);
                    NetworkServer.Destroy(enemy.gameObject); // No confirmed kill => no XP/loot or player kill credit.
                    continue;
                }
                var prefab = settings.Prefabs[reference.Clips[birth.ClipIndex].PrefabIndex];
                var body = NetworkServer.spawned[target.AvatarId].GetComponent<Rigidbody2D>();
                Vector2 forward = body != null ? body.linearVelocity.normalized : Vector2.zero;
                if (!TryReferencePosition(target, prefab, reference.RepositionDistance, out var destination, forward))
                {
                    referenceTrace?.WriteLine(FormattableString.Invariant($"{elapsed:R},reposition-position-failed,{birth.ClipIndex},{birth.SourceEnemy},{birth.Variant},,,{enemy.netId},Unchanged,,,,,,,{position.x:R},{position.y:R}"));
                    continue;
                }
                if (NetworkEnemySimulationWorld.Instance.RepositionReferenceEnemy(enemy, destination))
                {
                    offscreenSince[enemy.netId] = elapsed;
                    referenceTrace?.WriteLine(FormattableString.Invariant($"{elapsed:R},reposition,{birth.ClipIndex},{birth.SourceEnemy},{birth.Variant},,,{enemy.netId},Reset={birth.ResetOnReposition},,,,,,,{destination.x:R},{destination.y:R}"));
                }
            }
        }
    }
}
