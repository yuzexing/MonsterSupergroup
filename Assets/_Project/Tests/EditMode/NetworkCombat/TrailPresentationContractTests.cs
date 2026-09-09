using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using NUnit.Framework;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class TrailPresentationContractTests
    {
        private const ulong Root = 0x0011000300000002UL;
        private static NetworkTrailPresentationEdge Spawn() => new NetworkTrailPresentationEdge
        {
            SourcePlayerId = 17, WeaponId = 8, AttackEventId = Root, DashUseId = Root - 1,
            EventNetworkTime = 10, Phase = TrailPresentationPhase.Spawn, Position = new Vector2(5, 8),
            SamplingDuration = 1.25f, SegmentDuration = 0.625f, HitInterval = 1,
            Stats = new ProjectilePresentationStats { Duration = 1.25f, ProjectileCount = 1, BaseProjectileCount = 1 }
        };
        private static NetworkTrailPresentationEdge Point(uint index = 0, float elapsed = 0.2f)
        {
            var edge = Spawn(); edge.Phase = TrailPresentationPhase.Point;
            edge.EventNetworkTime += elapsed; edge.ElapsedSeconds = elapsed; edge.PointIndex = index;
            edge.Position = new Vector2(6 + index, 8);
            return edge;
        }
        private static NetworkTrailPresentationEdge End(bool terminate = false)
        {
            var edge = Spawn(); edge.EventNetworkTime = 12; edge.ElapsedSeconds = 1.3f;
            edge.Phase = terminate ? TrailPresentationPhase.Terminated : TrailPresentationPhase.SamplingEnded;
            return edge;
        }

        [Test]
        public void ReliableSourcePointsKeepTheirActualWorldPositionAndMonotonicIdentity()
        {
            var history = new TrailPresentationHistory();
            Assert.That(history.TryApply(Spawn()), Is.True);
            Assert.That(history.TryApply(Spawn()), Is.False);
            Assert.That(history.TryApply(Point(1)), Is.False);
            Assert.That(history.TryApply(Point()), Is.True);
            Assert.That(history.TryApply(Point()), Is.False);
            Assert.That(history.TryApply(Point(1, 0.1f)), Is.False);
            Assert.That(history.TryApply(Point(1, 0.3f)), Is.True);
            Assert.That(Point(1, 0.3f).ToPoint().WorldPosition, Is.EqualTo(new Vector2(7, 8)));
            Assert.That(history.TryApply(End()), Is.True);
            Assert.That(history.TryApply(Point(2, 1.4f)), Is.False);
            Assert.That(history.TryApply(End()), Is.False);
            Assert.That(history.TryApply(End(true)), Is.True);
            Assert.That(history.TryApply(End(true)), Is.False);
            history.RetireAttack(Root);
            Assert.That(history.AttackCount, Is.Zero);
            Assert.That(history.TryApply(Point()), Is.False);
        }

        [Test]
        public void EmptyAndCancelledTrailsCanFinishWithoutInventingAPoint()
        {
            var history = new TrailPresentationHistory();
            Assert.That(history.TryApply(Spawn()), Is.True);
            Assert.That(history.TryApply(End()), Is.True);
            Assert.That(history.TryApply(End(true)), Is.True);
            history.Clear();
            Assert.That(history.TryApply(Spawn()), Is.True);
            Assert.That(history.TryApply(End(true)), Is.True);
            Assert.That(history.TryApply(End()), Is.False);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void NonFiniteGeometryCannotReachParticles(float value)
        {
            var spawn = Spawn(); spawn.Position.x = value; Assert.That(spawn.IsValid, Is.False);
            spawn = Spawn(); spawn.SamplingDuration = value; Assert.That(spawn.IsValid, Is.False);
            spawn = Spawn(); spawn.SegmentDuration = value; Assert.That(spawn.IsValid, Is.False);
            spawn = Spawn(); spawn.HitInterval = value; Assert.That(spawn.IsValid, Is.False);
            var point = Point(); point.ElapsedSeconds = value; Assert.That(point.IsValid, Is.False);
            point = Point(); point.Position.y = value; Assert.That(point.IsValid, Is.False);
        }

        [Test]
        public void RootWeaponSourceAndClockMustMatchBeforeMutation()
        {
            var history = new TrailPresentationHistory(); history.TryApply(Spawn());
            var wrong = Point(); wrong.WeaponId++; Assert.That(history.TryApply(wrong), Is.False);
            wrong = Point(); wrong.SourcePlayerId++; Assert.That(history.TryApply(wrong), Is.False);
            wrong = Point(); wrong.AttackEventId++; Assert.That(history.TryApply(wrong), Is.False);
            wrong = Point(); wrong.EventNetworkTime = 9; Assert.That(history.TryApply(wrong), Is.False);
            Assert.That(history.TryApply(Point()), Is.True);
        }

        [Test]
        public void SamplingWindowRejectsLatePointsAndEarlyNaturalEnd()
        {
            var history = new TrailPresentationHistory(); history.TryApply(Spawn());
            Assert.That(history.TryApply(Point(0, 1.25f)), Is.False);
            var premature = End(); premature.ElapsedSeconds = 1;
            Assert.That(history.TryApply(premature), Is.False);
            Assert.That(history.TryApply(Point(0, 1.24f)), Is.True);
            Assert.That(history.TryApply(End()), Is.True);
        }

        [Test]
        public void TransportRoundTripPreservesEverySampleAndRejectsDuplicateBatch()
        {
            var batch = new NetworkTrailPresentationBatch { BatchSequence = 1, Edges = new[] { Spawn(), Point(), End(), End(true) } };
            var writer = new NetworkWriter(); writer.Write(batch);
            var copy = new NetworkReader(writer.ToArray()).Read<NetworkTrailPresentationBatch>();
            Assert.That(copy.Edges, Is.EqualTo(batch.Edges));
            Assert.That(NetworkWeaponCombatAdapter.IsValidTrailPresentationBatch(copy, 17, 0, 32), Is.True);
            Assert.That(NetworkWeaponCombatAdapter.IsValidTrailPresentationBatch(copy, 17, 1, 32), Is.False);
            Assert.That(NetworkWeaponCombatAdapter.IsValidTrailPresentationBatch(copy, 18, 0, 32), Is.False);
            Assert.That(NetworkWeaponCombatAdapter.IsValidTrailPresentationBatch(copy, 17, 0, 3), Is.False);
        }
    }
}
