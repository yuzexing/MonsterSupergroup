using System.Collections;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Traps;
using AstralShift.Managers;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class LimboTrapLifecycleTests
    {
        private GameObject pool, pauseObject;
        private PauseManager pause;
        private BarrierTrap authority, replica;
        private GameplayWaveRules rules;
        private uint slow;

        [SetUp]
        public void Setup()
        {
            Time.timeScale = 1;
            pool = new GameObject("Trap pool fixture"); pool.AddComponent<PoolManager>().Init();
            pauseObject = new GameObject("Trap pause fixture"); pause = pauseObject.AddComponent<PauseManager>();
            rules = Resources.Load<GameplayWaveRules>("LimboReference/SpatialBarrier"); Assert.That(rules, Is.Not.Null);
        }
        [TearDown]
        public void Teardown()
        {
            if (authority != null) { authority.CancelNetworkLifecycle(); Object.DestroyImmediate(authority.gameObject); }
            if (replica != null) { replica.CancelNetworkLifecycle(); Object.DestroyImmediate(replica.gameObject); }
            Object.DestroyImmediate(pool); Object.DestroyImmediate(pauseObject); PauseManager.Instance = null; Time.timeScale = 1;
        }
        private void Entry(bool active)
        {
            if (active && slow == 0) slow = pause.StartSlowMo(true, .25f);
            if (!active && slow != 0) { pause.StopSlowMo(true, slow); slow = 0; }
        }

        [UnityTest]
        public IEnumerator NativeBarrierShrinksDuringEntryAndReleasesOnlyAfterParticlesFinish()
        {
            authority = Object.Instantiate(rules.ReferenceBarrier);
            authority.ConfigureNetworkAuthority(Vector3.zero, .25f, Entry); authority.Init();
            int releases = 0; authority.onTrapEnd += () => releases++;
            Assert.That(authority.NetworkCollisionEnabled, Is.False);
            float deadline = Time.realtimeSinceStartup + 50;
            while (authority.NetworkPhase == BarrierPhase.Framing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(authority.NetworkPhase, Is.EqualTo(BarrierPhase.Building));
            Assert.That(authority.NetworkGroupCount, Is.EqualTo(90)); Assert.That(Time.timeScale, Is.EqualTo(.25f));
            var edge = authority.GetComponentInChildren<EdgeCollider2D>(); Physics2D.SyncTransforms();
            Assert.That(edge.points.Length, Is.EqualTo(41)); Assert.That(edge.OverlapPoint(Vector2.zero), Is.False);
            Assert.That(edge.OverlapPoint(new Vector2(19, 0)), Is.False); Assert.That(edge.OverlapPoint(new Vector2(20.1f, 0)), Is.True);
            pause.PauseGame();
            // Drain a WaitForSeconds already eligible in this frame before sampling the paused state.
            yield return null;
            int visible = authority.NetworkVisibleGroups; float radius = authority.NetworkInnerRadius;
            yield return new WaitForSecondsRealtime(.1f);
            Assert.That(authority.NetworkVisibleGroups, Is.EqualTo(visible)); Assert.That(authority.NetworkInnerRadius, Is.EqualTo(radius));
            pause.ResumeGame();
            while (authority != null && authority.NetworkPhase != BarrierPhase.Stopping && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(authority, Is.Not.Null); Assert.That(authority.NetworkPhase, Is.EqualTo(BarrierPhase.Stopping));
            Assert.That(authority.NetworkInnerRadius, Is.EqualTo(10).Within(.02)); Assert.That(Time.timeScale, Is.EqualTo(1));
            Assert.That(authority.NetworkCollisionEnabled, Is.False); Assert.That(releases, Is.Zero);
            while (releases == 0 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(releases, Is.EqualTo(1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator PauseValidationSeparatesNativeMotionFromDelayedPublishedState()
        {
            authority = Object.Instantiate(rules.ReferenceBarrier);
            authority.ConfigureNetworkAuthority(Vector3.zero, .25f, Entry); authority.Init();
            float deadline = Time.realtimeSinceStartup + 5;
            while (authority.NetworkPhase == BarrierPhase.Framing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(authority.NetworkPhase, Is.EqualTo(BarrierPhase.Building));
            pause.PauseGame(); yield return null;
            // Represent the last 20 Hz publication, before the last native step.
            var published = new ReferenceTrapSnapshot { Barrier = true, Center = Vector2.zero,
                Phase = BarrierPhase.Building, Radius = authority.NetworkInnerRadius + .006666667f,
                Count = authority.NetworkGroupCount, Visible = authority.NetworkVisibleGroups - 1 };
            var before = LimboSpatialObservation.CaptureBarrierState(published, authority);
            Assert.That(before.Radius, Is.Not.EqualTo(published.Radius));
            Assert.That(before.Visible, Is.Not.EqualTo(published.Visible));
            yield return new WaitForSecondsRealtime(.15f);
            var after = LimboSpatialObservation.CaptureBarrierState(published, authority);
            Assert.That(after, Is.EqualTo(before), "Native trap must remain frozen, including geometry and construction.");
            Assert.That(published.Radius, Is.GreaterThan(after.Radius), "Observation must not mutate or force network publication.");
            Assert.That(Time.timeScale, Is.Zero);
            pause.ResumeGame();
        }

        [UnityTest]
        public IEnumerator ReplicaDoesNotShrinkOrChangeClockAndCancellationPreservesPause()
        {
            replica = Object.Instantiate(rules.ReferenceBarrier);
            replica.ApplyNetworkPresentation(Vector3.zero, BarrierPhase.Shrinking, 15, 67, 67, .25f, true);
            yield return new WaitForSeconds(.15f);
            Assert.That(replica.NetworkInnerRadius, Is.EqualTo(15)); Assert.That(Time.timeScale, Is.EqualTo(1));
            replica.ApplyNetworkPresentation(Vector3.zero, BarrierPhase.Stopping, 10, 45, 45, .25f, false);
            Assert.That(replica.NetworkCollisionEnabled, Is.False);
            authority = Object.Instantiate(rules.ReferenceBarrier);
            authority.ConfigureNetworkAuthority(Vector3.zero, .25f, Entry); authority.Init();
            float deadline = Time.realtimeSinceStartup + 5;
            while (authority.NetworkPhase == BarrierPhase.Framing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(Time.timeScale, Is.EqualTo(.25f)); pause.PauseGame(); authority.CancelNetworkLifecycle();
            Assert.That(Time.timeScale, Is.Zero); Assert.That(authority.NetworkCollisionEnabled, Is.False);
            Assert.That(authority.NetworkGroupCount, Is.Zero); pause.ResumeGame(); Assert.That(Time.timeScale, Is.EqualTo(1));
        }
    }
}
