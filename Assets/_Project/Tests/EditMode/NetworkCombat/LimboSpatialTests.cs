using System;
using System.Linq;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.NetworkCombat.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class LimboSpatialTests
    {
        private static ReferenceWaveProgram Program(ReferenceSpawnDefinition[] clips, ReferenceBarrierDefinition[] barriers, bool validation = true) =>
            new ReferenceWaveProgram(clips, barriers, 100, 841.5766649882, AnimationCurve.Linear(0, 0, 1, 1),
                1.5f, 1, 2, 1.5f, 30, 5, 20, 6, 1.41f, 1, 1, validation);
        private static ReferenceBarrierDefinition Barrier() => new ReferenceBarrierDefinition { start = 1, end = 46,
            minimumRadius = 10, maximumRadius = 20, shrinkDuration = 30, sides = 40,
            lifecycleVersion = 1, readiness = ReferenceEnemyReadiness.ValidationPending, readinessNote = "Fixture" };

        [Test]
        public void OffscreenProcessingUsesEveryViewGraceAndIndependentDistanceOrTimeout()
        {
            var rules = Program(Array.Empty<ReferenceSpawnDefinition>(), Array.Empty<ReferenceBarrierDefinition>());
            var views = new[] { new Bounds(Vector3.zero, new Vector3(20, 12, 0)),
                new Bounds(new Vector3(30, 0), new Vector3(20, 12, 0)) };
            float visibleToClient = GameplayCameraGeometry.MinimumOutsideDistance(new Vector2(30, 0), views);
            Assert.That(visibleToClient, Is.Zero);
            Assert.That(rules.OffscreenProcessingDue(0, 100, 30, visibleToClient), Is.False);
            float outside = GameplayCameraGeometry.MinimumOutsideDistance(new Vector2(45, 0), views);
            Assert.That(outside, Is.EqualTo(5));
            Assert.That(rules.OffscreenProcessingDue(0, 29.99, 20, outside), Is.False);
            Assert.That(rules.OffscreenProcessingDue(0, 34.99, 30, outside), Is.False);
            Assert.That(rules.OffscreenProcessingDue(0, 35, 30, outside), Is.True);
            Assert.That(rules.OffscreenProcessingDue(0, 35, 34, outside), Is.False, "Re-entering any view must reset the continuous timer.");
            float far = GameplayCameraGeometry.MinimumOutsideDistance(new Vector2(60, 0), views);
            Assert.That(far, Is.EqualTo(20)); Assert.That(rules.OffscreenProcessingDue(0, 30, 30, far), Is.True);
            Assert.That(rules.OffscreenProcessingDue(0, 100, 30,
                GameplayCameraGeometry.MinimumOutsideDistance(Vector2.zero, Array.Empty<Bounds>())), Is.False);
        }

        [Test]
        public void RemoteViewUsesMapClampingAndExpandedFramingCanPreventReposition()
        {
            var map = new Bounds(Vector3.zero, new Vector3(100, 70, 0));
            var edge = GameplayCameraGeometry.ClampView(new Bounds(new Vector3(49, 34), new Vector3(20, 12, 0)), map);
            Assert.That(edge.center, Is.EqualTo(new Vector3(40, 29, 0)));
            Assert.That(GameplayCameraGeometry.MinimumOutsideDistance(new Vector2(25, 0),
                new[] { new Bounds(Vector3.zero, new Vector3(20, 12, 0)) }), Is.GreaterThan(0));
            Assert.That(GameplayCameraGeometry.MinimumOutsideDistance(new Vector2(25, 0),
                new[] { new Bounds(Vector3.zero, new Vector3(60, 40, 0)) }), Is.Zero);
        }

        [Test]
        public void PauseDuringEachFormationDelayPreservesCaptureSpawnAndReleaseDeadlines()
        {
            var clip = new ReferenceSpawnDefinition { Mode = ReferenceSpawnMode.FormationBurst, SourceEnemy = "fixture", Start = 0, End = 5, Count = 1 };
            var schedule = new ServerWaveSchedule("pause", new WaveParameters(Program(new[] { clip }, Array.Empty<ReferenceBarrierDefinition>()), Array.Empty<GameObject>(), 1000, 100), 0);
            var alive = new int[1]; int captures = 0; schedule.FormationCaptured += _ => captures++;
            schedule.TickReference(.01, 1, true, 0, 0, alive, out _);
            schedule.TickReference(.5, 2, false, 0, 0, alive, out _);
            schedule.TickReference(10, 3, false, 0, 0, alive, out _);
            schedule.TickReference(10.1, 4, true, 0, 0, alive, out _);
            Assert.That(captures, Is.Zero);
            schedule.TickReference(11.1, 5, true, 0, 0, alive, out _);
            Assert.That(captures, Is.EqualTo(1));
            schedule.TickReference(11.5, 6, false, 0, 0, alive, out _);
            schedule.TickReference(20, 7, true, 0, 0, alive, out _);
            Assert.That(schedule.State.TotalAttempts, Is.Zero);
            Assert.That(schedule.TickReference(21.01, 8, true, 0, 0, alive, out var spawn), Is.True);
            schedule.Resolve(spawn, true); Assert.That(schedule.ReferenceTrapCount, Is.EqualTo(1));
        }

        [Test]
        public void FormationKeepsTrapSlotAfterSpawnUntilOriginalDurationAndReleasesOnce()
        {
            var clip = new ReferenceSpawnDefinition { SourceEnemy = "fixture", Mode = ReferenceSpawnMode.FormationBurst, Start = 0, End = 15, Count = 2 };
            var schedule = new ServerWaveSchedule("B", new WaveParameters(Program(new[] { clip }, Array.Empty<ReferenceBarrierDefinition>()), Array.Empty<GameObject>(), 1000, 100), 0);
            int releases = 0; schedule.FormationReleased += _ => releases++;
            var alive = new int[1]; schedule.TickReference(.01, 1, true, 1000, 1000, alive, out _);
            schedule.TickReference(1.01, 2, true, 1000, 1000, alive, out _);
            for (int i = 0; i < 2; i++)
            { Assert.That(schedule.TickReference(2.02, 3, true, 1000+i, 1000, alive, out var spawn), Is.True); schedule.Resolve(spawn, true); }
            Assert.That(schedule.State.ActiveClips, Is.Zero);
            Assert.That(schedule.TryAcquireBarrierSlot(), Is.False);
            schedule.TickReference(15, 4, true, 1002, 1000, alive, out _); Assert.That(releases, Is.Zero);
            schedule.TickReference(15.02, 5, true, 1002, 1000, alive, out _); Assert.That(releases, Is.EqualTo(1));
            Assert.That(schedule.TryAcquireBarrierSlot(), Is.True);
            schedule.TickReference(16, 6, true, 1002, 1000, alive, out _); Assert.That(releases, Is.EqualTo(1));
            Assert.That(schedule.ReferenceTrapCount, Is.EqualTo(1));
            schedule.Stop(); Assert.That(schedule.ReferenceTrapCount, Is.Zero);
            Assert.That(schedule.FormationReleaseTime(0), Is.Zero);
        }

        [Test]
        public void BarrierRetriesInitAndProgressThenOneSecondWithoutCatchupOrForcedClipEnd()
        {
            var schedule = new ServerWaveSchedule("barrier", new WaveParameters(Program(Array.Empty<ReferenceSpawnDefinition>(), new[] { Barrier() }), Array.Empty<GameObject>(), 1000, 100), 0);
            var alive = Array.Empty<int>(); int rolls = 0, births = 0;
            float Roll() { rolls++; return 100; }
            bool Spawn(int _) { births++; return true; }
            schedule.TickReference(1.01, 1, true, 0, 0, alive, out _); schedule.TickReferenceBarriers(1, Roll, Spawn);
            Assert.That(rolls, Is.EqualTo(2));
            schedule.TickReferenceBarriers(1, Roll, Spawn); Assert.That(rolls, Is.EqualTo(2));
            schedule.TickReference(1.5, 2, true, 0, 0, alive, out _); schedule.TickReferenceBarriers(2, Roll, Spawn); Assert.That(rolls, Is.EqualTo(2));
            schedule.TickReference(10, 3, true, 0, 0, alive, out _); schedule.TickReferenceBarriers(3, Roll, Spawn); Assert.That(rolls, Is.EqualTo(3));
            Assert.That(schedule.TryAcquireBarrierSlot(), Is.True);
            schedule.TickReference(47, 4, true, 0, 0, alive, out _); schedule.TickReferenceBarriers(4, Roll, Spawn); Assert.That(rolls, Is.EqualTo(3));
            schedule.ReleaseBarrierSlot();
            schedule.TickReference(48, 5, true, 0, 0, alive, out _); schedule.TickReferenceBarriers(5, () => 0, Spawn);
            Assert.That(births, Is.EqualTo(1), "A delayed source trap can start after its configured end.");
            schedule.ReleaseBarrierSlot();
            schedule.TickReference(49, 6, true, 0, 0, alive, out _); schedule.TickReferenceBarriers(6, () => 0, Spawn);
            Assert.That(births, Is.EqualTo(1));
        }

        [Test]
        public void OccupiedBarrierAttemptDoesNotRollOrIncreaseChance()
        {
            var schedule = new ServerWaveSchedule("waiting", new WaveParameters(Program(Array.Empty<ReferenceSpawnDefinition>(), new[] { Barrier() }), Array.Empty<GameObject>(), 1000, 100), 0);
            var decisions = new System.Collections.Generic.List<(string result, float before, float after, float roll)>();
            schedule.ReferenceBarrierDecision += (i, result, before, after, roll) => decisions.Add((result, before, after, roll));
            Assert.That(schedule.TryAcquireBarrierSlot(), Is.True);
            int rolls = 0;
            schedule.TickReference(1.01, 1, true, 0, 0, Array.Empty<int>(), out _);
            schedule.TickReferenceBarriers(1, () => { rolls++; return 100; }, _ => true);
            Assert.That(rolls, Is.Zero);
            Assert.That(decisions.All(d => d.result == "Occupied" && d.before == d.after && float.IsNaN(d.roll)), Is.True);
            schedule.ReleaseBarrierSlot();
            schedule.TickReference(2.02, 2, true, 0, 0, Array.Empty<int>(), out _);
            schedule.TickReferenceBarriers(2, () => { rolls++; return 100; }, _ => true);
            Assert.That(rolls, Is.EqualTo(1));
            Assert.That(decisions.Last().result, Is.EqualTo("RollFailed"));
            Assert.That(decisions.Last().after, Is.EqualTo(decisions.Last().before * 2).Within(.0001));
        }

        [Test]
        public void LegacyAndUnvalidatedBarrierCannotBeEnabledByDefaultEnumValue()
        {
            var legacy = Barrier(); legacy.lifecycleVersion = 0; legacy.readiness = ReferenceEnemyReadiness.Ready;
            Assert.That(Program(Array.Empty<ReferenceSpawnDefinition>(), new[] { legacy }).ReadinessError(), Does.Contain("barrier gate"));
            Assert.That(Program(Array.Empty<ReferenceSpawnDefinition>(), new[] { Barrier() }, false).ReadinessError(), Does.Contain("barrier gate"));
            Assert.That(Program(Array.Empty<ReferenceSpawnDefinition>(), new[] { Barrier() }).ReadinessError(), Is.Null);
        }

        [Test]
        public void SpatialAssetsRetainSixLifetimeStreamsAndIndependentFullGate()
        {
            var rules = AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot + "/SpatialBarrier.asset");
            Assert.That(rules.TryCapture(out var captured, out var error), Is.True, error);
            Assert.That(captured.Reference.ReadinessError(), Is.Null);
            Assert.That(rules.ReferenceBarrier.NumberOfSides, Is.EqualTo(40));
            var edge = rules.ReferenceBarrier.GetComponentInChildren<EdgeCollider2D>(true);
            Assert.That(edge.edgeRadius, Is.EqualTo(30)); Assert.That(edge.excludeLayers.value, Is.EqualTo(448));
            var streams = rules.ReferenceFormationWarning.GetComponentsInChildren<ParticleSystem>(true);
            Assert.That(streams.Length, Is.EqualTo(6));
            Assert.That(streams.Select(p => p.main.startLifetime.constantMax).ToArray(), Is.EqualTo(new[] { 2f, 2f, 2f, .3f, .3f, 1f }));
            foreach (var ps in streams)
            {
                Assert.That(ps.main.cullingMode, Is.EqualTo(ParticleSystemCullingMode.AlwaysSimulate));
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                bool groundGlow = ps.name == "Glow" || ps.name == "GlowFlat";
                Assert.That(renderer.sortingLayerName, Is.EqualTo(groundGlow ? "BackgroundFront" : "Props"), ps.name);
                Assert.That(renderer.sortingOrder, Is.EqualTo(groundGlow ? 100 : 0), ps.name);
            }
            var full = AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot + "/Full.asset");
            Assert.That(full.TryCapture(out var all, out error), Is.True, error); all.Reference.FlowReadiness = ReferenceEnemyReadiness.ImplementationPending; Assert.That(all.Reference.ReadinessError(), Is.Not.Null);
            Assert.That(all.Reference.Clips.Where(c => c.Mode == ReferenceSpawnMode.FormationBurst).All(c => c.SpawnReadiness == ReferenceEnemyReadiness.Ready), Is.True);
        }

        [Test]
        public void TrapProtectionRemovalDoesNotRemoveUpgradeOrUltimateProtection()
        {
            var ledger = new CombatLedger(); ledger.RegisterEntity(1, 500, CombatEntityKind.Player, CombatEntityAuthority.OwnerFinal, 1);
            ledger.SetPlayerUpgradeSelectionState(1, true); ledger.SetPlayerTrapInvulnerable(1, true);
            ledger.SetPlayerTrapInvulnerable(1, false); ledger.TryGetState(1, out var selecting); Assert.That(selecting.AbsoluteInvulnerable, Is.True);
            ledger.SetPlayerUltimateInvulnerable(1, true); ledger.SetPlayerUpgradeSelectionState(1, false);
            ledger.SetPlayerTrapInvulnerable(1, true); ledger.SetPlayerTrapInvulnerable(1, false);
            ledger.TryGetState(1, out var ultimate); Assert.That(ultimate.AbsoluteInvulnerable, Is.True);
            ledger.SetPlayerUltimateInvulnerable(1, false); ledger.TryGetState(1, out var normal); Assert.That(normal.AbsoluteInvulnerable, Is.False);
        }

        [Test]
        public void TrapStateRoundTripsThroughMirrorIncludingRoundAndSlotSeparateFromCollision()
        {
            var before = new ReferenceTrapSnapshot { Round = 7, Target = 55, Barrier = true, Occupied = true,
                Collision = false, Phase = AstralShift.HellMaiden.Combat.Traps.BarrierPhase.Stopping,
                Center = new Vector2(-3, 5), BeganAt = 123.45, ReleaseAt = 150, Radius = 10, Count = 45, Visible = 45, EntryScale = .25f };
            var writer = new NetworkWriter(); writer.Write(before);
            var reader = new NetworkReader(writer.ToArraySegment()); var after = reader.Read<ReferenceTrapSnapshot>();
            Assert.That(after, Is.EqualTo(before));
            // These same snapshots are embedded in graphical evidence records.
            Assert.That(JsonUtility.FromJson<ReferenceTrapSnapshot>(JsonUtility.ToJson(before)), Is.EqualTo(before));
        }
    }
}
