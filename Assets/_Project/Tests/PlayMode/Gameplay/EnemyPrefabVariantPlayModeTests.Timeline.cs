using System.Collections;
using System.Linq;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using Mirror;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed partial class EnemyPrefabVariantPlayModeTests
    {
        [UnityTest]
        public IEnumerator LostSoulCancellationRestoresLocalBodyAfterActiveWithoutReplayingDamage()
        {
            var enemy = SpawnLostSoul();
            yield return WaitFor(() => Ready(enemy));
            var agent = enemy.GetComponent<NetworkEnemySimulationAgent>();
            var playback = enemy.GetComponent<NetworkEnemyMeleeReplica>();
            var attack = enemy.GetComponent<EnemyAttackExplosion>();
            enemy.SuspendSimulationExecution(); agent.enabled = false;
            agent.Authority.ApplyRole(EnemySimulationRole.Replica, 999, Owner.netId, agent.Assignment.Epoch);
            var state = LostSoulState(enemy, 1.1);
            state.Phase = EnemyAttackPresentationPhase.Active;
            playback.ApplyAction(state, agent.Assignment.Epoch, EnemySimulationClock.CombatNow);
            var body = enemy.collider;
            Assert.That(body.enabled, Is.False);
            Assert.That(attack.LocalDamageEnabled, Is.True);
            state.Phase = EnemyAttackPresentationPhase.Cancelled;
            playback.ApplyAction(state, agent.Assignment.Epoch, EnemySimulationClock.CombatNow);
            Assert.That(body.enabled, Is.True, "A living cancelled explosion must restore its body, including on observers.");
            Assert.That(attack.LocalDamageEnabled, Is.False);
            Assert.That(attack.ExplosionInstance, Is.Null);
            state.Phase = EnemyAttackPresentationPhase.Active;
            playback.ApplyAction(state, agent.Assignment.Epoch, EnemySimulationClock.CombatNow);
            Assert.That(attack.LocalDamageEnabled, Is.False, "Old Active state cannot reverse accepted cancellation.");
            NetworkServer.Destroy(enemy.gameObject);
        }

        [UnityTest]
        public IEnumerator AHitAdmittedBeforeAnyActionCannotCancelANewerSharedAction()
        {
            var enemy = Spawn("NetworkEnemySkeleton");
            yield return WaitFor(() => Ready(enemy));
            var agent = enemy.GetComponent<NetworkEnemySimulationAgent>();
            var world = NetworkEnemySimulationWorld.Instance;
            agent.SetServerAssignment(world.Registry.AssignServerAuthoritative(agent.netId, Owner.netId));
            var attack = enemy.GetComponent<EnemyAttackMelee>();
            attack.enemyAnimator = enemy.enemyAnimator;
            var state = EnemyActionTimeline.Begin(((ulong)agent.Assignment.Epoch << 32) | 400,
                EnemySimulationClock.CombatNow, attack.TimelineStrikes, attack.RecoveryTime,
                enemy.attackCooldown, Vector2.right, enemy.transform.position + Vector3.right);
            enemy.RestoreSimulationAction(state, EnemySimulationClock.CombatNow);
            var preset = ScriptableObject.CreateInstance<AstralShift.HellMaiden.Player.Attacks.KnockbackSettings>();
            try
            {
                preset.distance = 1; preset.speedMultiplier = 6; preset.staggerTime = .6f;
                preset.speedCurve = AnimationCurve.Linear(0, 0, 1, 1);
                var command = new EnemyKnockbackCommand {
                    Kind = EnemyKnockbackKind.Ultimate, EnemyEntityId = agent.netId,
                    AssignmentEpoch = agent.Assignment.Epoch, SourcePlayerId = Owner.netId,
                    AbilityCombatId = 0x80000001, RootEventId = 1, CommandId = 100,
                    IssuedAt = EnemySimulationClock.Now, Origin = enemy.transform.position - Vector3.right,
                    Settings = EnemyKnockbackSettings.From(preset), InterruptedActionId = 0 };
                Assert.That(command.IsValid, Is.True);
                Assert.That(agent.TryApplyKnockback(command, 0, true), Is.False,
                    "Zero identifies no action at admission; it must not be a wildcard for a later attack.");
                Assert.That(enemy.CaptureSimulationAction(EnemySimulationClock.CombatNow).Phase,
                    Is.EqualTo(EnemyAttackPresentationPhase.Warning));
                command.CommandId++; command.InterruptedActionId = state.ActionId;
                Assert.That(agent.TryApplyKnockback(command, 0, true), Is.True);
                Assert.That(enemy.CaptureSimulationAction(EnemySimulationClock.CombatNow).Phase,
                    Is.EqualTo(EnemyAttackPresentationPhase.Cancelled));
            }
            finally { Object.Destroy(preset); NetworkServer.Destroy(enemy.gameObject); }
        }

        [UnityTest]
        public IEnumerator LostSoulUsesLocalCenterAndRetainsItAcrossEpochWithoutAnActiveEdge()
        {
            var enemy = SpawnLostSoul();
            yield return WaitFor(() => Ready(enemy));
            var agent = enemy.GetComponent<NetworkEnemySimulationAgent>();
            var playback = enemy.GetComponent<NetworkEnemyMeleeReplica>();
            var attack = enemy.GetComponent<EnemyAttackExplosion>();
            enemy.SuspendSimulationExecution(); agent.enabled = false;
            agent.Authority.ApplyRole(EnemySimulationRole.Replica, 999, Owner.netId, agent.Assignment.Epoch);
            double now = EnemySimulationClock.CombatNow;
            var state = LostSoulState(enemy, 1.1);
            state.Phase = EnemyAttackPresentationPhase.Warning;
            state.ExplosionTriggered = state.SelfDestructPending = false;
            state.ExplosionPosition = new Vector2(-100, -100);
            playback.ApplyAction(state, agent.Assignment.Epoch, now);
            Vector2 local = (Vector2)enemy.transform.position + attack.ExplosionOffset;
            Assert.That(attack.ExplosionInstance, Is.Not.Null);
            Assert.That(attack.LocalExplosionCenter, Is.EqualTo(local));
            Assert.That(attack.LocalDamageEnabled, Is.True);
            var instance = attack.ExplosionInstance;
            enemy.transform.position += new Vector3(3, 1);
            playback.ApplyAction(state, agent.Assignment.Epoch + 1, now);
            Assert.That(attack.ExplosionInstance, Is.SameAs(instance));
            Assert.That((Vector2)instance.transform.position, Is.EqualTo(local));
            playback.ApplyAction(state, agent.Assignment.Epoch + 1, state.ActiveUntil + .01);
            Assert.That(attack.LocalDamageEnabled, Is.False);
            playback.ApplyAction(state, agent.Assignment.Epoch + 1, now);
            Assert.That(attack.LocalDamageEnabled, Is.False, "An older phase cannot rewind Recovery.");
            NetworkServer.Destroy(enemy.gameObject);
        }

        [UnityTest]
        public IEnumerator SharedHostHasOneAttackAndDamageGuardRejectsAnExpiredEnabledCollider()
        {
            var enemy = Spawn("NetworkEnemySkeleton");
            yield return WaitFor(() => Ready(enemy) && !Owner.GetComponent<AstralShift.HellMaiden.Player.PlayerMovement>().IsInvulnerable);
            var agent = enemy.GetComponent<NetworkEnemySimulationAgent>();
            var playback = enemy.GetComponent<NetworkEnemyMeleeReplica>();
            var attack = enemy.GetComponent<EnemyAttackMelee>();
            double now = EnemySimulationClock.CombatNow;
            var state = EnemyActionTimeline.Begin(789, now - .6, attack.TimelineStrikes, attack.RecoveryTime, enemy.attackCooldown, Vector2.right, Vector2.zero);
            state.WarningUntil = now - .02; state.ActiveUntil = now - .001; state.RecoveryUntil = now + .2;
            playback.ApplyAction(state, agent.Assignment.Epoch, now - .01);
            Assert.That(playback.DamageWindowActive, Is.True);
            int hp = Owner.GetComponent<AstralShift.HellMaiden.Player.PlayerCombatantBinding>().CurrentHealth;
            attack.LocalDamageInteraction.Interact(Owner.GetComponentInChildren<PlayerHitbox>());
            attack.LocalDamageInteraction.SettlePendingCollisions();
            Assert.That(Owner.GetComponent<AstralShift.HellMaiden.Player.PlayerCombatantBinding>().CurrentHealth, Is.EqualTo(hp));
            Assert.That(enemy.GetComponentsInChildren<EnemyAttackPrefab>().Count(p => p.damageInteraction != null && p.damageInteraction.isActiveAndEnabled), Is.EqualTo(1));
            NetworkServer.Destroy(enemy.gameObject);
        }

        [UnityTest]
        public IEnumerator SkeletonWarningAloneAdvancesLocalWindowWithoutAnActiveMessage()
        {
            var enemy = Spawn("NetworkEnemySkeleton");
            yield return WaitFor(() => Ready(enemy));
            var agent = enemy.GetComponent<NetworkEnemySimulationAgent>();
            var playback = enemy.GetComponent<NetworkEnemyMeleeReplica>();
            enemy.SuspendSimulationExecution();
            agent.enabled = false;
            agent.Authority.ApplyRole(EnemySimulationRole.Replica, 999, Owner.netId, agent.Assignment.Epoch);
            double start = EnemySimulationClock.CombatNow;
            var action = new EnemyActionState
            {
                ActionId = ((ulong)agent.Assignment.Epoch << 32) | 77,
                Phase = EnemyAttackPresentationPhase.Warning,
                WarningStartedAt = start, WarningUntil = start + .57,
                ActiveUntil = start + .65, RecoveryUntil = start + .94, NextAttackAt = start + 1.44,
                Facing = Vector2.right, TargetPosition = enemy.transform.position + Vector3.right
            };
            var edge = new EnemyAttackPresentationEdge
            {
                EnemyEntityId = agent.netId, AssignmentEpoch = agent.Assignment.Epoch, StateSequence = 100,
                Phase = action.Phase, Facing = action.Facing, StateStartNetworkTime = start, PhaseDuration = .57f,
                Checkpoint = new EnemySimulationCheckpoint { Movement = new EnemySimulationSnapshot
                    { EnemyEntityId = agent.netId, AssignmentEpoch = agent.Assignment.Epoch,
                      Runtime = new EnemySimulationRuntimeState { Action = action } } }
            };
            Assert.That(agent.ReceiveRemoteAttackPresentation(edge), Is.True);
            Assert.That(playback.DamageWindowActive, Is.False);
            bool observedActive = false;
            while (EnemySimulationClock.CombatNow < action.RecoveryUntil)
            {
                if (playback.DamageWindowActive)
                {
                    observedActive = true;
                    Assert.That(enemy.GetComponentsInChildren<EnemyAttackPrefab>()
                        .Count(p => p.damageInteraction != null && p.damageInteraction.isActiveAndEnabled), Is.EqualTo(1));
                }
                yield return null;
            }
            Assert.That(observedActive, Is.True, "A received Warning already defines the 0.08-second Active window; no Active message was sent.");
            Assert.That(playback.DamageWindowActive, Is.False);
            edge.StateSequence++;
            Assert.That(agent.ReceiveRemoteAttackPresentation(edge), Is.True);
            yield return null;
            Assert.That(playback.DamageWindowActive, Is.False, "A delayed Warning must not restart an expired hit window.");
            NetworkServer.Destroy(enemy.gameObject);
        }
    }
}
