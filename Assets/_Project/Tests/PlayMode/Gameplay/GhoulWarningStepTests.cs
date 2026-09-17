using System.Collections;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class GhoulWarningStepTests
    {
        private GameObject root, wall;
        private EnemyController controller;
        private SequenceEnemyAttack attack;
        private EnemyWarningStep step;
        private Rigidbody2D body;
        private SimulationMode2D previousPhysics;
        private float previousScale;

        [SetUp] public void Setup()
        {
            previousPhysics = Physics2D.simulationMode; previousScale = Time.timeScale;
            Physics2D.simulationMode = SimulationMode2D.Script;
            root = new GameObject("Ghoul warning physics fixture"); root.SetActive(false);
            root.transform.position = new Vector3(3000, 3000);
            body = root.AddComponent<Rigidbody2D>(); body.gravityScale = 0;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            controller = root.AddComponent<EnemyController>(); controller.enabled = false;
            controller.stats = new EnemyStats();
            controller.stats.Init(new EnemyStatsValues { Health = 50, Damage = 50, Speed = 6 });
            root.AddComponent<EnemyCombatantBinding>().InitializeFromStats(controller.stats);
            var movement = root.AddComponent<EnemyDefaultMovement>(); movement.SetRigidBody(body);
            controller._currentMovementScript = movement;
            attack = root.AddComponent<SequenceEnemyAttack>(); attack.controller = controller;
            controller.attackScript = attack; controller.ConfigureSimulationClock(() => 10, 1);
            step = root.AddComponent<EnemyWarningStep>(); step.Configure(attack, body, .4f);
            root.SetActive(true);
            Assert.That(controller.IsAlive,Is.True,"A motion fixture must use the real Combatant life baseline.");
        }

        [TearDown] public void Cleanup()
        {
            Object.DestroyImmediate(root); if (wall != null) Object.DestroyImmediate(wall);
            Physics2D.simulationMode = previousPhysics; Time.timeScale = previousScale;
        }

        private EnemyActionState Begin()
        {
            var state = EnemyActionTimeline.Begin(42, 10, new[] {
                new EnemyStrikeTiming(.292857170f,.235714287f), new EnemyStrikeTiming(.378571451f,.2f),
                new EnemyStrikeTiming(.207142860f,.207142860f) }, .121428572f, .5f, Vector2.right, Vector2.zero);
            attack.PrepareTimeline(ref state); return state;
        }

        [UnityTest] public IEnumerator SourceRequestsAt30Fps() => SourceRequests(30);
        [UnityTest] public IEnumerator SourceRequestsAt60Fps() => SourceRequests(60);
        [UnityTest] public IEnumerator SourceRequestsAt144Fps() => SourceRequests(144);
        [UnityTest] public IEnumerator SourceRequestsAt60FpsWithQuarterSpeedClock() => SourceRequests(60,.25f);
        private IEnumerator SourceRequests(int fps, float scale = 1)
        {
            Time.timeScale = scale;
            var state = Begin(); var origin = body.position;
            double physicsDebt = 0; int lastStrike = -1; bool sawActive = false;
            for (int tick = 1; tick <= fps * 1.7 / scale; tick++)
            {
                yield return null;
                double now = 10 + (double)tick / fps * scale;
                state = EnemyActionTimeline.Resolve(state, now);
                if (state.Phase == EnemyAttackPresentationPhase.Warning && state.StrikeIndex != lastStrike)
                {
                    lastStrike = state.StrikeIndex; state.PoseStrikeIndex = lastStrike;
                    state.LockedStrikeMask |= (byte)(1 << lastStrike);
                    state.Facing = lastStrike == 1 ? Vector2.up : Vector2.right;
                }
                var before = body.position;
                attack.ApplySimulationFrame(state, now); attack.CaptureSimulationMotion(ref state);
                if (state.Phase == EnemyAttackPresentationPhase.Warning)
                {
                    // Independent transcription of the recovered per-render-tick rule.
                    float fraction = Mathf.Clamp01((float)((now-state.WarningStartedAt)/(state.WarningUntil-state.WarningStartedAt)));
                    var expected = before + state.Facing * (.4f * fraction);
                    Assert.That(Vector2.Distance(state.WarningStep.RequestedPosition, expected), Is.LessThan(.0005));
                    Assert.That(body.constraints, Is.EqualTo(RigidbodyConstraints2D.FreezeRotation));
                }
                else if (state.Phase == EnemyAttackPresentationPhase.Active || state.Phase == EnemyAttackPresentationPhase.Recovery)
                {
                    sawActive = true;
                    Assert.That(body.constraints, Is.EqualTo(RigidbodyConstraints2D.FreezeAll));
                }
                physicsDebt += scale/fps;
                while (physicsDebt >= .02) { Physics2D.Simulate(.02f); physicsDebt -= .02; }
                if (state.Phase == EnemyAttackPresentationPhase.Active || state.Phase == EnemyAttackPresentationPhase.Recovery)
                    Assert.That(Vector2.Distance(body.position,before), Is.LessThan(.001), "Active/recovery cannot continue stepping.");
            }
            Assert.That(sawActive, Is.True); Assert.That(lastStrike, Is.EqualTo(2));
            Assert.That(state.WarningStep.StartedMask, Is.EqualTo(7)); Assert.That(state.WarningStep.CompletedMask, Is.EqualTo(7));
            Assert.That(Vector2.Distance(body.position, origin), Is.GreaterThan(.4));
            TestContext.WriteLine($"Controlled render={fps}, scale={scale}, physics=50Hz, displacement={body.position-origin}; frame dependent by source design.");
        }

        [UnityTest] public IEnumerator PauseAndRepeatedTimeDoNotStepAndCancelDiscardsPendingMove()
        {
            var state = Begin(); yield return null;
            state = EnemyActionTimeline.Resolve(state,10.1); var origin = body.position;
            Time.timeScale = 0; attack.ApplySimulationFrame(state,10.1); attack.CaptureSimulationMotion(ref state);
            Assert.That(state.WarningStep.StartedMask, Is.Zero);
            Time.timeScale = 1; attack.ApplySimulationFrame(state,10.1); attack.CaptureSimulationMotion(ref state);
            var requested = state.WarningStep.RequestedPosition;
            attack.ApplySimulationFrame(state,10.2); attack.CaptureSimulationMotion(ref state);
            Assert.That(state.WarningStep.RequestedPosition,Is.EqualTo(requested),"One render frame cannot execute two steps.");
            // Keep Unity's fixed-time cursor aligned with this test's manual physics:
            // no fixed step has committed the pending MovePosition yet.
            Time.timeScale = 0;
            yield return null;
            Time.timeScale = 1;
            attack.ApplySimulationFrame(state,10.1); attack.CaptureSimulationMotion(ref state);
            Assert.That(state.WarningStep.RequestedPosition,Is.EqualTo(requested),"Paused combat time cannot accumulate motion.");
            attack.SuspendSimulation(); Physics2D.Simulate(.02f);
            Assert.That(Vector2.Distance(body.position,origin),Is.LessThan(.001));
        }

        [UnityTest] public IEnumerator HandoffPreservesPendingPositionAndDoesNotReplayAnExpiredWarningExit()
        {
            var state = Begin(); yield return null;
            state = EnemyActionTimeline.Resolve(state,10.1);
            attack.ApplySimulationFrame(state,10.1); attack.CaptureSimulationMotion(ref state);
            var requested = state.WarningStep.RequestedPosition;
            attack.ReleaseSimulationMotion(); attack.RestoreSimulationMotion(state,10.1);
            Physics2D.Simulate(.02f);
            Assert.That(Vector2.Distance(body.position,requested),Is.LessThan(.001));
            // A delayed handoff is already Active: no historical ticks or +0.4 exit.
            state.WarningStep.Pending = false;
            state = EnemyActionTimeline.Resolve(state,10.4);
            attack.ReleaseSimulationMotion(); attack.RestoreSimulationMotion(state,10.4);
            var origin = body.position;
            yield return null;
            attack.ApplySimulationFrame(state,10.41); attack.CaptureSimulationMotion(ref state); Physics2D.Simulate(.02f);
            Assert.That(body.constraints,Is.EqualTo(RigidbodyConstraints2D.FreezeAll));
            Assert.That(Vector2.Distance(body.position,origin),Is.LessThan(.001));
            Assert.That(state.WarningStep.CompletedMask & 1,Is.EqualTo(1));
            attack.RestoreSimulationMotion(state,10.41); Physics2D.Simulate(.02f);
            Assert.That(Vector2.Distance(body.position,origin),Is.LessThan(.001));
        }

        [UnityTest] public IEnumerator WarningRequestsRemainSubjectToActualPhysicsCollision()
        {
            root.AddComponent<CircleCollider2D>().radius = .46f;
            wall = new GameObject("Warning step wall"); wall.transform.position = root.transform.position + Vector3.right;
            wall.AddComponent<BoxCollider2D>().size = new Vector2(.2f,10);
            Physics2D.SyncTransforms(); var origin = body.position; var state = Begin();
            for (int i=1;i<=14;i++)
            {
                yield return null; double now=10+i*.02;
                state=EnemyActionTimeline.Resolve(state,now); attack.ApplySimulationFrame(state,now); Physics2D.Simulate(.02f);
            }
            Assert.That(body.position.x-origin.x,Is.InRange(.1f,.46f));
        }
    }
}
