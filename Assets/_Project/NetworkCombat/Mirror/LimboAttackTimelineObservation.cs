using System;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Interactions;
using AstralShift.HellMaiden.Player;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    // Passive detailed evidence. Packet suppression is opt-in and restricted to explicit mechanism fixtures.
    public sealed class LimboAttackTimelineObservation : MonoBehaviour
    {
        private LimboObservationLog log;
        private static LimboAttackTimelineObservation instance;
        private void Awake()
        {
            Directory.CreateDirectory(LimboReferenceLaunch.OutputDirectory);
            log = new LimboObservationLog(Path.Combine(LimboReferenceLaunch.OutputDirectory, "attack-timeline.jsonl"));
            instance = this;
            NetworkEnemyMeleeReplica.TimelineObserved += Observed;
            PlayerDamageInteraction.DamageAttempted += Hit;
        }
        internal static bool SuppressPhaseEdge(EnemyAttackPresentationEdge edge)
        {
            bool suppress = instance != null && !LimboReferenceLaunch.Manual && LimboReferenceLaunch.Profile.EndsWith("-fixture", StringComparison.Ordinal) &&
                LimboReferenceLaunch.Argument("--limbo-attack-edges=") == "warning-only" &&
                (edge.Phase == EnemyAttackPresentationPhase.Active || edge.Phase == EnemyAttackPresentationPhase.Recovery);
            if (suppress) instance.Write("suppressed-phase-edge", new Row { enemy = edge.EnemyEntityId, epoch = edge.AssignmentEpoch,
                action = edge.Checkpoint.Movement.Runtime.Action, detail = "Explicit network fixture; Warning/poses and cancellation remain delivered." });
            return suppress;
        }
        private void Observed(NetworkEnemyMeleeReplica playback, string kind)
        {
            var agent = playback.GetComponent<NetworkEnemySimulationAgent>();
            var controller = playback.GetComponent<EnemyController>();
            var attack = controller.attackScript;
            var area = attack.LocalDamageInteraction;
            var polygon = area != null ? area.GetComponent<PolygonCollider2D>() : null;
            var soul = attack as EnemyAttackExplosion;
            Write(kind, new Row { enemy = agent.netId, epoch = agent.Assignment.Epoch, source = agent.Birth.SourceEnemy,
                variant = agent.Birth.Variant, simulator = agent.Authority.RunsCombatDecisions, action = playback.AppliedAction,
                position = agent.transform.position, damageEnabled = playback.DamageWindowActive,
                instance = attack.LocalAttackInstance != null ? attack.LocalAttackInstance.GetInstanceID() : 0,
                damage = controller.stats.Damage, statsBound = area == null || ReferenceEquals(area.enemyStats, controller.stats),
                localExplosionCenter = soul != null ? soul.LocalExplosionCenter : default,
                vertices = polygon != null ? polygon.GetPath(0).Select(p => (Vector2)polygon.transform.TransformPoint(p + polygon.offset)).ToArray() : Array.Empty<Vector2>() });
        }
        private void Hit(PlayerDamageInteraction source, PlayerMovement player, int amount)
        {
            var enemy = source.GetComponentInParent<NetworkEnemySimulationAgent>();
            if (enemy == null || enemy.GetComponent<EnemyController>().attackScript?.SupportsSharedTimeline != true) return;
            var stamp = source.LastAttackContact;
            Write("damage-attempt", new Row { enemy = enemy.netId, epoch = stamp.Epoch, source = enemy.Birth.SourceEnemy,
                variant = enemy.Birth.Variant, player = player.GetComponent<NetworkIdentity>().netId, damage = amount,
                contactAction = stamp.ActionId, strike = stamp.StrikeIndex, generation = stamp.Generation, contactAt = stamp.ContactAt,
                action = enemy.GetComponent<NetworkEnemyMeleeReplica>().AppliedAction, damageEnabled = source.isActiveAndEnabled });
        }
        private void Write(string kind, Row row)
        {
            row.kind = kind; row.combat = EnemySimulationClock.CombatNow; row.realtime = Time.realtimeSinceStartupAsDouble;
            row.round = NetworkEnemySimulationWorld.CurrentRound;
            row.run = NetworkCombatWorld.Instance != null ? NetworkCombatWorld.Instance.GetComponent<NetworkWaveProgress>().Snapshot.RunId : null;
            log?.WriteLine(JsonUtility.ToJson(row));
        }
        private void OnDestroy()
        {
            NetworkEnemyMeleeReplica.TimelineObserved -= Observed; PlayerDamageInteraction.DamageAttempted -= Hit;
            if (instance == this) instance = null;
            log?.Dispose();
        }
        [Serializable] private class Row
        {
            public string kind, detail, run, source;
            public uint round, enemy, epoch, player, generation;
            public int variant, instance, damage, strike;
            public ulong contactAction;
            public double combat, realtime, contactAt;
            public bool simulator, damageEnabled, statsBound;
            public Vector2 position, localExplosionCenter;
            public Vector2[] vertices;
            public EnemyActionState action;
        }
    }
}
