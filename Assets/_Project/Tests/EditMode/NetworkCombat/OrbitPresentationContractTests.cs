using System.Collections.Generic;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class OrbitPresentationContractTests
    {
        private const uint Source = 17, Weapon = 6;
        private const ulong Root = 0x0011000300000001UL;

        [Test]
        public void OrbIdentitySeparatesRootsAndOrdinals()
        {
            var keys = new HashSet<OrbitPresentationKey>
            {
                new OrbitPresentationKey(Root, 0), new OrbitPresentationKey(Root, 1),
                new OrbitPresentationKey(Root + 1, 0), new OrbitPresentationKey(Root, 0)
            };
            Assert.That(keys.Count, Is.EqualTo(3));
            Assert.That(default(OrbitPresentationKey).IsValid, Is.False);
        }

        [TestCase(1, 0, true)]
        [TestCase(2, 1, true)]
        [TestCase(2, 2, false)]
        [TestCase(0, 0, false)]
        [TestCase(-1, 0, false)]
        [TestCase(65536, 65535, true)]
        [TestCase(65537, 0, false)]
        public void SpawnValidatesTheFullOrbIndexRange(int count, int index, bool expected)
        {
            var edge = Spawn(); edge.OrbCount = count; edge.OrbIndex = (ushort)index;
            Assert.That(edge.IsValid, Is.EqualTo(expected));
        }

        [TestCase(0f, true)]
        [TestCase(0.01f, true)]
        [TestCase(-1f, false)]
        [TestCase(float.NaN, false)]
        [TestCase(float.PositiveInfinity, false)]
        [TestCase(float.NegativeInfinity, false)]
        public void OrbitDurationAndHideElapsedMustBeNonnegativeAndFinite(float value, bool expected)
        {
            var edge = Spawn(); edge.OrbitDuration = value;
            Assert.That(edge.IsValid, Is.EqualTo(expected));
            edge = Hide(); edge.OrbitElapsedSeconds = value;
            Assert.That(edge.IsValid, Is.EqualTo(expected));
        }

        [Test]
        public void InvalidOrOverflowingGeometryCannotReachLocalTransforms()
        {
            foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                var edge = Spawn(); edge.InitialPhaseRadians = value; Assert.That(edge.IsValid, Is.False);
                edge = Spawn(); edge.Radius = value; Assert.That(edge.IsValid, Is.False);
                edge = Spawn(); edge.AngularSpeedRadians = value; Assert.That(edge.IsValid, Is.False);
            }
            var negative = Spawn(); negative.Radius = -1; Assert.That(negative.IsValid, Is.False);
            var overflow = Spawn(); overflow.AngularSpeedRadians = float.MaxValue;
            Assert.That(overflow.IsValid, Is.False);
            var stationary = Spawn(); stationary.Radius = 0; stationary.AngularSpeedRadians = 0;
            Assert.That(stationary.IsValid, Is.True);
            stationary.AngularSpeedRadians = -2;
            Assert.That(stationary.IsValid, Is.True, "A finite authored clockwise orbit is representable.");
        }

        [Test]
        public void EveryPhaseRequiresAnIdentityAndFiniteTimestamp()
        {
            foreach (var valid in new[] { Spawn(), Hide(), End() })
            {
                Assert.That(valid.IsValid, Is.True);
                var edge = valid; edge.SourcePlayerId = 0; Assert.That(edge.IsValid, Is.False);
                edge = valid; edge.WeaponId = 0; Assert.That(edge.IsValid, Is.False);
                edge = valid; edge.AttackEventId = 0; Assert.That(edge.IsValid, Is.False);
                edge = valid; edge.Phase = (OrbitPresentationPhase)255; Assert.That(edge.IsValid, Is.False);
                foreach (double time in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                {
                    edge = valid; edge.EventNetworkTime = time; Assert.That(edge.IsValid, Is.False);
                }
            }
            var malformed = Spawn(); malformed.Stats.Duration = float.NaN;
            Assert.That(malformed.IsValid, Is.False);
            malformed = Spawn(); malformed.Stats.ProjectileCount = 0;
            Assert.That(malformed.IsValid, Is.False);
        }

        [Test]
        public void HistoryFreezesRootGeometryAndStatsWithoutConsumingRejectedOrdinals()
        {
            var history = new OrbitPresentationHistory();
            Assert.That(history.TrySpawn(Spawn()), Is.True);
            Assert.That(history.TrySpawn(Spawn()), Is.False);
            var next = Spawn(index: 1); next.WeaponId++; Assert.That(history.TrySpawn(next), Is.False);
            next = Spawn(index: 1); next.OrbCount++; Assert.That(history.TrySpawn(next), Is.False);
            next = Spawn(index: 1); next.Radius++; Assert.That(history.TrySpawn(next), Is.False);
            next = Spawn(index: 1); next.AngularSpeedRadians++; Assert.That(history.TrySpawn(next), Is.False);
            next = Spawn(index: 1); next.OrbitDuration++; Assert.That(history.TrySpawn(next), Is.False);
            next = Spawn(index: 1); next.Stats.SizeMultiplierSum++; Assert.That(history.TrySpawn(next), Is.False);
            Assert.That(history.TrySpawn(Spawn(index: 1)), Is.True);
            Assert.That(history.AttackCount, Is.EqualTo(1));
        }

        [Test]
        public void HideAndEndCannotAffectAnUnspawnedWrongWeaponOrAlreadyEndedOrb()
        {
            var history = new OrbitPresentationHistory();
            Assert.That(history.TryHide(Hide()), Is.False);
            Assert.That(history.TryTerminate(End()), Is.False);
            Assert.That(history.TrySpawn(Spawn()), Is.True);
            Assert.That(history.TryHide(Hide(index: 1)), Is.False);
            var early = Hide(); early.OrbitElapsedSeconds = 2.9f;
            Assert.That(history.TryHide(early), Is.False);
            var wrong = Hide(); wrong.WeaponId++;
            Assert.That(history.TryHide(wrong), Is.False);
            var overflow = Hide(); overflow.OrbitElapsedSeconds = float.MaxValue;
            Assert.That(history.TryHide(overflow), Is.False);
            Assert.That(history.TryHide(Hide()), Is.True);
            Assert.That(history.TryHide(Hide()), Is.False);
            wrong = End(); wrong.WeaponId++; Assert.That(history.TryTerminate(wrong), Is.False);
            Assert.That(history.TryTerminate(End()), Is.True);
            Assert.That(history.TryTerminate(End()), Is.False);
            Assert.That(history.TryHide(Hide()), Is.False);
            Assert.That(history.TrySpawn(Spawn()), Is.False);
            // Early synchronous cancellation of one orb cannot consume its not-yet-spawned sibling.
            Assert.That(history.TrySpawn(Spawn(index: 1)), Is.True);
            Assert.That(history.TryTerminate(End(index: 1)), Is.True, "Cancellation does not require a Hide edge.");
            Assert.That(history.TryHide(Hide(index: 1)), Is.False);
        }

        [Test]
        public void HideValidatesTheTargetOrbPhaseInsteadOfItsFirstSiblingsPhase()
        {
            var history = new OrbitPresentationHistory();
            var first = Spawn(); first.OrbitDuration = 0; first.AngularSpeedRadians = 1;
            var sibling = first; sibling.OrbIndex = 1; sibling.InitialPhaseRadians = float.MaxValue;
            Assert.That(history.TrySpawn(first), Is.True);
            Assert.That(history.TrySpawn(sibling), Is.True);
            var hidden = Hide(index: 1); hidden.OrbitElapsedSeconds = float.MaxValue;
            Assert.That(history.TryHide(hidden), Is.False);
            hidden.OrbitElapsedSeconds = 0;
            Assert.That(history.TryHide(hidden), Is.True, "A rejected phase must not consume the legal Hide edge.");
        }

        [Test]
        public void RootRetirementAndClearDoNotLeaveAnyPhasePermission()
        {
            var history = new OrbitPresentationHistory();
            Assert.That(history.TrySpawn(Spawn()), Is.True);
            Assert.That(history.TrySpawn(Spawn(Root + 1)), Is.True);
            history.RetireAttack(Root);
            Assert.That(history.AttackCount, Is.EqualTo(1));
            Assert.That(history.TryHide(Hide()), Is.False);
            Assert.That(history.TryTerminate(End()), Is.False);
            Assert.That(history.TryHide(Hide(Root + 1)), Is.True);
            history.Clear();
            Assert.That(history.AttackCount, Is.Zero);
            Assert.That(history.TryTerminate(End(Root + 1)), Is.False);
            Assert.That(history.TrySpawn(Hide()), Is.False);
            Assert.That(history.TryHide(Spawn()), Is.False);
            Assert.That(history.TryTerminate(Spawn()), Is.False);
            Assert.That(history.AttackCount, Is.Zero);
        }

        [Test]
        public void BatchesRejectOtherOwnersReplaysMissingEdgesAndCapacityOverflow()
        {
            var batch = new NetworkOrbitPresentationBatch { BatchSequence = 1, Edges = new[] { Spawn(), Hide(), End() } };
            Assert.That(Valid(batch), Is.True);
            Assert.That(NetworkWeaponCombatAdapter.IsValidOrbitPresentationBatch(batch, Source + 1, 0, 32), Is.False);
            Assert.That(NetworkWeaponCombatAdapter.IsValidOrbitPresentationBatch(batch, Source, 1, 32), Is.False);
            Assert.That(NetworkWeaponCombatAdapter.IsValidOrbitPresentationBatch(batch, Source, 2, 32), Is.False);
            Assert.That(NetworkWeaponCombatAdapter.IsValidOrbitPresentationBatch(batch, Source, 0, 2), Is.False);
            Assert.That(NetworkWeaponCombatAdapter.IsValidOrbitPresentationBatch(batch, Source, 0, 0), Is.False);
            Assert.That(NetworkWeaponCombatAdapter.IsValidOrbitPresentationBatch(batch, 0, 0, 32), Is.False);
            Assert.That(NetworkWeaponCombatAdapter.IsValidOrbitPresentationBatch(batch, Source, uint.MaxValue, 32), Is.True);
            batch.Edges[1].SourcePlayerId++; Assert.That(Valid(batch), Is.False);
            batch.Edges[1] = Hide(); batch.BatchSequence = 0; Assert.That(Valid(batch), Is.False);
            batch.BatchSequence = 1; batch.Edges = new NetworkOrbitPresentationEdge[0]; Assert.That(Valid(batch), Is.False);
            batch.Edges = null; Assert.That(Valid(batch), Is.False);
        }

        [Test]
        public void MirrorPreservesOrbitParametersIdentityAndActualHidePhase()
        {
            var last = Spawn(index: ushort.MaxValue); last.OrbCount = ushort.MaxValue + 1;
            var batch = new NetworkOrbitPresentationBatch
            {
                BatchSequence = 77, Edges = new[] { last, Hide(index: ushort.MaxValue), End(index: ushort.MaxValue) }
            };
            var writer = new NetworkWriter(); writer.Write(batch);
            var reader = new NetworkReader(writer.ToArraySegment());
            var decoded = reader.Read<NetworkOrbitPresentationBatch>();
            Assert.That(decoded.BatchSequence, Is.EqualTo(batch.BatchSequence));
            Assert.That(decoded.Edges, Is.EqualTo(batch.Edges));
            Assert.That(Valid(decoded), Is.True);
            Assert.That(decoded.Edges[0].ToSpawn().Stats, Is.EqualTo(last.Stats));
            Assert.That(decoded.Edges[0].ToSpawn().InitialPhaseRadians, Is.EqualTo(last.InitialPhaseRadians));
            Assert.That(decoded.Edges[0].ToSpawn().Radius, Is.EqualTo(2.2f));
            Assert.That(decoded.Edges[0].ToSpawn().AngularSpeedRadians, Is.EqualTo(2f));
            Assert.That(decoded.Edges[0].ToSpawn().OrbitDuration, Is.EqualTo(3.14f));
            Assert.That(decoded.Edges[1].ToHiding().OrbitElapsedSeconds, Is.EqualTo(3.16f));
            Assert.That(decoded.Edges[2].ToTermination().Key, Is.EqualTo(last.Key));
            Assert.That(reader.Remaining, Is.Zero);
        }

        private static bool Valid(NetworkOrbitPresentationBatch batch) =>
            NetworkWeaponCombatAdapter.IsValidOrbitPresentationBatch(batch, Source, 0, 32);
        private static NetworkOrbitPresentationEdge Spawn(ulong root = Root, ushort index = 0) => new NetworkOrbitPresentationEdge
        {
            SourcePlayerId = Source, WeaponId = Weapon, AttackEventId = root, OrbIndex = index, OrbCount = 2,
            EventNetworkTime = 5.25, Phase = OrbitPresentationPhase.Spawn, InitialPhaseRadians = index * 3.1415927f,
            Radius = 2.2f, AngularSpeedRadians = 2, OrbitDuration = 3.14f,
            Stats = new ProjectilePresentationStats { Duration = 3.14f, ProjectileCount = 2, BaseProjectileCount = 2 }
        };
        private static NetworkOrbitPresentationEdge Hide(ulong root = Root, ushort index = 0) => new NetworkOrbitPresentationEdge
        {
            SourcePlayerId = Source, WeaponId = Weapon, AttackEventId = root, OrbIndex = index,
            EventNetworkTime = 8.41, Phase = OrbitPresentationPhase.Hiding, OrbitElapsedSeconds = 3.16f
        };
        private static NetworkOrbitPresentationEdge End(ulong root = Root, ushort index = 0) => new NetworkOrbitPresentationEdge
        {
            SourcePlayerId = Source, WeaponId = Weapon, AttackEventId = root, OrbIndex = index,
            EventNetworkTime = 9.21, Phase = OrbitPresentationPhase.Terminated
        };
    }
}
