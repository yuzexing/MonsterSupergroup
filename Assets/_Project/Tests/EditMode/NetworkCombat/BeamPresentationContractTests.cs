using System.Collections.Generic;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using NUnit.Framework;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class BeamPresentationContractTests
    {
        private const uint Source = 17;
        private const uint Weapon = 3;
        private const ulong Root = 0x0011000300000001UL;

        [Test]
        public void KeysDistinguishBeamIndicesAndAttackRoots()
        {
            var keys = new HashSet<BeamPresentationKey>
            {
                new BeamPresentationKey(Root, 0), new BeamPresentationKey(Root, 1),
                new BeamPresentationKey(Root + 1, 0), new BeamPresentationKey(Root, 0)
            };
            Assert.That(keys.Count, Is.EqualTo(3));
            Assert.That(default(BeamPresentationKey).IsValid, Is.False);
        }

        [Test]
        public void SpawnAndAimConversionsPreserveFrozenVisualDataAndNormalizeHeading()
        {
            NetworkBeamPresentationEdge edge = Spawn();
            edge.RootDirection = new Vector2(3, 4);
            Assert.That(edge.IsValid, Is.True);
            BeamPresentationSpawn spawn = edge.ToSpawn();
            Assert.That(spawn.WeaponId, Is.EqualTo(Weapon));
            Assert.That(spawn.Key, Is.EqualTo(edge.Key));
            Assert.That(spawn.BeamCount, Is.EqualTo(3));
            Assert.That(spawn.Element, Is.EqualTo(AttackElement.Poison));
            Assert.That(spawn.RootDirection, Is.EqualTo(new Vector2(0.6f, 0.8f)));
            Assert.That(spawn.AnimationDuration, Is.EqualTo(1.5f));
            Assert.That(spawn.Stats, Is.EqualTo(edge.Stats));
            NetworkBeamPresentationAim networkAim = Aim();
            networkAim.RootDirection = new Vector2(-3, 4);
            Assert.That(networkAim.IsValid, Is.True);
            BeamPresentationAim aim = networkAim.ToAim();
            Assert.That(aim.WeaponId, Is.EqualTo(Weapon));
            Assert.That(aim.AttackEventId, Is.EqualTo(Root));
            Assert.That(aim.RootDirection, Is.EqualTo(new Vector2(-0.6f, 0.8f)));
        }

        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(2, true)]
        [TestCase(-1, false)]
        [TestCase(3, false)]
        [TestCase(256, false)]
        [TestCase(257, false)]
        [TestCase(258, false)]
        [TestCase(int.MinValue, false)]
        [TestCase(int.MaxValue, false)]
        public void ElementValidationDoesNotTruncateAnInvalidIntegerToAValidByte(int element, bool expected)
        {
            NetworkBeamPresentationEdge edge = Spawn();
            edge.Element = (AttackElement)element;
            Assert.That(edge.IsValid, Is.EqualTo(expected));
        }

        [TestCase(1, 0, true)]
        [TestCase(3, 2, true)]
        [TestCase(3, 3, false)]
        [TestCase(0, 0, false)]
        [TestCase(-1, 0, false)]
        [TestCase(65536, 65535, true)]
        [TestCase(65537, 0, false)]
        public void BeamCountAndIndexUseTheFullUnsignedShortIndexRange(int count, int index, bool expected)
        {
            NetworkBeamPresentationEdge edge = Spawn();
            edge.BeamCount = count;
            edge.BeamIndex = (ushort)index;
            Assert.That(edge.IsValid, Is.EqualTo(expected));
        }

        [TestCase(0.01f, true)]
        [TestCase(0f, true)]
        [TestCase(-1f, false)]
        [TestCase(float.NaN, false)]
        [TestCase(float.PositiveInfinity, false)]
        [TestCase(float.NegativeInfinity, false)]
        public void SpawnRequiresANonnegativeFiniteAnimationDuration(float duration, bool expected)
        {
            NetworkBeamPresentationEdge edge = Spawn();
            edge.AnimationDuration = duration;
            Assert.That(edge.IsValid, Is.EqualTo(expected));
        }

        [Test]
        public void SpawnRejectsInvalidIdentityPhaseTimeDirectionAndStats()
        {
            NetworkBeamPresentationEdge edge = Spawn();
            edge.SourcePlayerId = 0;
            Assert.That(edge.IsValid, Is.False);
            edge = Spawn(); edge.WeaponId = 0;
            Assert.That(edge.IsValid, Is.False);
            edge = Spawn(); edge.AttackEventId = 0;
            Assert.That(edge.IsValid, Is.False);
            edge = Spawn(); edge.Phase = (BeamPresentationPhase)255;
            Assert.That(edge.IsValid, Is.False);
            foreach (double time in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                edge = Spawn(); edge.EventNetworkTime = time;
                Assert.That(edge.IsValid, Is.False);
            }
            foreach (Vector2 direction in InvalidDirections())
            {
                edge = Spawn(); edge.RootDirection = direction;
                Assert.That(edge.IsValid, Is.False, "Rejected heading: " + direction);
            }
            edge = Spawn(); edge.Stats.Duration = float.NaN;
            Assert.That(edge.IsValid, Is.False);
            edge = Spawn(); edge.Stats.SizeMultiplierSum = float.PositiveInfinity;
            Assert.That(edge.IsValid, Is.False);
            edge = Spawn(); edge.Stats.EffectiveSpeed = -1;
            Assert.That(edge.IsValid, Is.False);
            edge = Spawn(); edge.Stats.ProjectileCount = 0;
            Assert.That(edge.IsValid, Is.False);
            edge = Spawn(); edge.Stats.BaseProjectileCount = 0;
            Assert.That(edge.IsValid, Is.False);
        }

        [Test]
        public void TerminationNeedsIdentityAndFiniteTimeWithoutNeedingSpawnOnlyFields()
        {
            NetworkBeamPresentationEdge edge = End();
            Assert.That(edge.IsValid, Is.True);
            Assert.That(edge.BeamCount, Is.Zero);
            Assert.That(edge.Stats.ProjectileCount, Is.Zero);
            Assert.That(edge.ToTermination().Key, Is.EqualTo(Spawn().Key));
            Assert.That(edge.ToTermination().WeaponId, Is.EqualTo(Weapon));
            edge.SourcePlayerId = 0;
            Assert.That(edge.IsValid, Is.False);
            edge = End(); edge.WeaponId = 0;
            Assert.That(edge.IsValid, Is.False);
            edge = End(); edge.AttackEventId = 0;
            Assert.That(edge.IsValid, Is.False);
            edge = End(); edge.EventNetworkTime = double.NaN;
            Assert.That(edge.IsValid, Is.False);
        }

        [Test]
        public void AimRejectsInvalidIdentityNonfiniteTimeAndDegenerateHeading()
        {
            NetworkBeamPresentationAim aim = Aim();
            aim.SourcePlayerId = 0;
            Assert.That(aim.IsValid, Is.False);
            aim = Aim(); aim.WeaponId = 0;
            Assert.That(aim.IsValid, Is.False);
            aim = Aim(); aim.AttackEventId = 0;
            Assert.That(aim.IsValid, Is.False);
            foreach (double time in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                aim = Aim(); aim.EventNetworkTime = time;
                Assert.That(aim.IsValid, Is.False);
            }
            foreach (Vector2 direction in InvalidDirections())
            {
                aim = Aim(); aim.RootDirection = direction;
                Assert.That(aim.IsValid, Is.False);
            }
        }

        [Test]
        public void HistoryDeduplicatesEachBeamAndFreezesRootWeaponCountAndElement()
        {
            var history = new BeamPresentationHistory();
            Assert.That(history.TrySpawn(Spawn()), Is.True);
            Assert.That(history.TrySpawn(Spawn()), Is.False);
            NetworkBeamPresentationEdge next = Spawn(index: 1);
            next.WeaponId++;
            Assert.That(history.TrySpawn(next), Is.False);
            next = Spawn(index: 1); next.BeamCount++;
            Assert.That(history.TrySpawn(next), Is.False);
            next = Spawn(index: 1); next.Element = AttackElement.Fire;
            Assert.That(history.TrySpawn(next), Is.False);
            Assert.That(history.TrySpawn(Spawn(index: 1)), Is.True,
                "A rejected mutation must not consume the valid beam ordinal.");
            Assert.That(history.AttackCount, Is.EqualTo(1));
            Assert.That(history.CanAim(Weapon, Root), Is.True);
            Assert.That(history.CanAim(Weapon + 1, Root), Is.False);
        }

        [Test]
        public void HistoryAllowsRemainingSpawnAfterEarlyCompletionButNeverReplaysATerminatedKey()
        {
            var history = new BeamPresentationHistory();
            Assert.That(history.TrySpawn(Spawn()), Is.True);
            Assert.That(history.TryTerminate(End()), Is.True);
            Assert.That(history.CanAim(Weapon, Root), Is.False);
            Assert.That(history.TrySpawn(Spawn()), Is.False);
            Assert.That(history.TryTerminate(End()), Is.False);
            // An animation can finish synchronously while the same attack is still spawning its other beams.
            Assert.That(history.TrySpawn(Spawn(index: 1)), Is.True);
            Assert.That(history.TrySpawn(Spawn(index: 2)), Is.True);
            Assert.That(history.TryTerminate(End(index: 1)), Is.True);
            Assert.That(history.CanAim(Weapon, Root), Is.True);
            Assert.That(history.TryTerminate(End(index: 2)), Is.True);
            Assert.That(history.CanAim(Weapon, Root), Is.False);
            for (ushort i = 0; i < 3; i++) Assert.That(history.TrySpawn(Spawn(index: i)), Is.False);
        }

        [Test]
        public void HistorySeparatesRootsAndRetiresOnlyTheCompletedAttack()
        {
            var history = new BeamPresentationHistory();
            Assert.That(history.TrySpawn(Spawn()), Is.True);
            NetworkBeamPresentationEdge other = Spawn(Root + 1);
            other.WeaponId = 9;
            other.BeamCount = 1;
            other.Element = AttackElement.Fire;
            Assert.That(history.TrySpawn(other), Is.True);
            Assert.That(history.AttackCount, Is.EqualTo(2));
            NetworkBeamPresentationEdge wrongEnd = End(); wrongEnd.WeaponId++;
            Assert.That(history.TryTerminate(wrongEnd), Is.False);
            Assert.That(history.CanAim(Weapon, Root), Is.True);
            Assert.That(history.TryTerminate(End(index: 2)), Is.False, "Unspawned indices cannot terminate another beam.");
            history.RetireAttack(Root);
            Assert.That(history.AttackCount, Is.EqualTo(1));
            Assert.That(history.CanAim(Weapon, Root), Is.False);
            Assert.That(history.TryTerminate(End()), Is.False);
            Assert.That(history.CanAim(9, Root + 1), Is.True);
            Assert.That(history.TrySpawn(other), Is.False);
            history.RetireAttack(Root);
            Assert.That(history.AttackCount, Is.EqualTo(1));
            history.Clear();
            Assert.That(history.AttackCount, Is.Zero);
            Assert.That(history.CanAim(9, Root + 1), Is.False);
            Assert.That(history.TrySpawn(default), Is.False);
            Assert.That(history.TrySpawn(End()), Is.False);
            Assert.That(history.TryTerminate(Spawn()), Is.False);
            Assert.That(history.AttackCount, Is.Zero);
        }

        [Test]
        public void EdgeBatchRejectsOtherOwnersMalformedEdgesReplaysAndCapacityOverflow()
        {
            var batch = new NetworkBeamPresentationBatch { BatchSequence = 1, Edges = new[] { Spawn(), End() } };
            Assert.That(Valid(batch), Is.True);
            Assert.That(NetworkWeaponCombatAdapter.IsValidBeamPresentationBatch(batch, Source + 1, 0, 32), Is.False);
            Assert.That(NetworkWeaponCombatAdapter.IsValidBeamPresentationBatch(batch, 0, 0, 32), Is.False);
            Assert.That(NetworkWeaponCombatAdapter.IsValidBeamPresentationBatch(batch, Source, 1, 32), Is.False);
            Assert.That(NetworkWeaponCombatAdapter.IsValidBeamPresentationBatch(batch, Source, 2, 32), Is.False);
            Assert.That(NetworkWeaponCombatAdapter.IsValidBeamPresentationBatch(batch, Source, 0, 1), Is.False);
            Assert.That(NetworkWeaponCombatAdapter.IsValidBeamPresentationBatch(batch, Source, 0, 0), Is.False);
            batch.Edges[1].SourcePlayerId++;
            Assert.That(Valid(batch), Is.False, "Every edge must belong to the sender, including termination edges.");
            batch.Edges[1] = End(); batch.Edges[0].Element = (AttackElement)257;
            Assert.That(Valid(batch), Is.False);
        }

        [Test]
        public void AimBatchRejectsOtherOwnersMalformedAimReplaysAndCapacityOverflow()
        {
            var batch = new NetworkBeamAimBatch { BatchSequence = 1, Aims = new[] { Aim(), Aim(Root + 1) } };
            Assert.That(Valid(batch), Is.True);
            Assert.That(NetworkWeaponCombatAdapter.IsValidBeamAimBatch(batch, Source + 1, 0, 32), Is.False);
            Assert.That(NetworkWeaponCombatAdapter.IsValidBeamAimBatch(batch, 0, 0, 32), Is.False);
            Assert.That(NetworkWeaponCombatAdapter.IsValidBeamAimBatch(batch, Source, 1, 32), Is.False);
            Assert.That(NetworkWeaponCombatAdapter.IsValidBeamAimBatch(batch, Source, 2, 32), Is.False);
            Assert.That(NetworkWeaponCombatAdapter.IsValidBeamAimBatch(batch, Source, 0, 1), Is.False);
            Assert.That(NetworkWeaponCombatAdapter.IsValidBeamAimBatch(batch, Source, 0, 0), Is.False);
            batch.Aims[1].SourcePlayerId++;
            Assert.That(Valid(batch), Is.False);
            batch.Aims[1] = Aim(Root + 1); batch.Aims[1].RootDirection.x = float.NaN;
            Assert.That(Valid(batch), Is.False);
        }

        [Test]
        public void BothBatchTypesAllowSequenceWrapAndRejectZeroSequenceOrMissingEntries()
        {
            var edges = new NetworkBeamPresentationBatch { BatchSequence = 1, Edges = new[] { Spawn() } };
            var aims = new NetworkBeamAimBatch { BatchSequence = 1, Aims = new[] { Aim() } };
            Assert.That(NetworkWeaponCombatAdapter.IsValidBeamPresentationBatch(edges, Source, uint.MaxValue, 32), Is.True);
            Assert.That(NetworkWeaponCombatAdapter.IsValidBeamAimBatch(aims, Source, uint.MaxValue, 32), Is.True);
            edges.BatchSequence = aims.BatchSequence = 0;
            Assert.That(Valid(edges), Is.False);
            Assert.That(Valid(aims), Is.False);
            edges.BatchSequence = aims.BatchSequence = 1;
            edges.Edges = new NetworkBeamPresentationEdge[0]; aims.Aims = new NetworkBeamPresentationAim[0];
            Assert.That(Valid(edges), Is.False);
            Assert.That(Valid(aims), Is.False);
            edges.Edges = null; aims.Aims = null;
            Assert.That(Valid(edges), Is.False);
            Assert.That(Valid(aims), Is.False);
        }

        [Test]
        public void MirrorRoundTripPreservesBatchesRootIdsBeamOrdinalsAndVisualStats()
        {
            NetworkBeamPresentationEdge last = Spawn(index: ushort.MaxValue);
            last.BeamCount = ushort.MaxValue + 1;
            var edges = new NetworkBeamPresentationBatch { BatchSequence = 91, Edges = new[] { last, End(index: ushort.MaxValue) } };
            var aims = new NetworkBeamAimBatch { BatchSequence = 28, Aims = new[] { Aim(), Aim(Root + 1) } };
            var writer = new NetworkWriter();
            writer.Write(edges);
            writer.Write(aims);
            var reader = new NetworkReader(writer.ToArraySegment());
            var decodedEdges = reader.Read<NetworkBeamPresentationBatch>();
            var decodedAims = reader.Read<NetworkBeamAimBatch>();
            Assert.That(decodedEdges.BatchSequence, Is.EqualTo(edges.BatchSequence));
            Assert.That(decodedEdges.Edges, Is.EqualTo(edges.Edges));
            Assert.That(decodedEdges.Edges[0].AttackEventId, Is.EqualTo(Root));
            Assert.That(decodedEdges.Edges[0].BeamCount, Is.EqualTo(65536));
            Assert.That(decodedEdges.Edges[0].BeamIndex, Is.EqualTo(ushort.MaxValue));
            Assert.That(decodedEdges.Edges[0].Stats, Is.EqualTo(last.Stats));
            Assert.That(decodedAims.BatchSequence, Is.EqualTo(aims.BatchSequence));
            Assert.That(decodedAims.Aims, Is.EqualTo(aims.Aims));
            Assert.That(Valid(decodedEdges), Is.True);
            Assert.That(Valid(decodedAims), Is.True);
            Assert.That(reader.Remaining, Is.Zero);
        }

        [Test]
        public void MirrorRoundTripDoesNotDisguiseOutOfRangeElementAsAValidByte()
        {
            NetworkBeamPresentationEdge edge = Spawn();
            edge.Element = (AttackElement)257;
            var writer = new NetworkWriter();
            writer.Write(edge);
            var reader = new NetworkReader(writer.ToArraySegment());
            NetworkBeamPresentationEdge decoded = reader.Read<NetworkBeamPresentationEdge>();
            Assert.That((int)decoded.Element, Is.EqualTo(257));
            Assert.That(decoded.IsValid, Is.False);
            Assert.That(reader.Remaining, Is.Zero);
        }

        private static bool Valid(NetworkBeamPresentationBatch batch) =>
            NetworkWeaponCombatAdapter.IsValidBeamPresentationBatch(batch, Source, 0, 32);
        private static bool Valid(NetworkBeamAimBatch batch) =>
            NetworkWeaponCombatAdapter.IsValidBeamAimBatch(batch, Source, 0, 32);

        private static IEnumerable<Vector2> InvalidDirections() => new[]
        {
            Vector2.zero, new Vector2(0.000001f, 0), new Vector2(float.NaN, 1),
            new Vector2(1, float.NegativeInfinity), new Vector2(float.PositiveInfinity, 1),
            new Vector2(float.MaxValue, float.MaxValue)
        };

        private static NetworkBeamPresentationEdge Spawn(ulong root = Root, ushort index = 0) => new NetworkBeamPresentationEdge
        {
            SourcePlayerId = Source, WeaponId = Weapon, AttackEventId = root, BeamIndex = index, BeamCount = 3,
            EventNetworkTime = 5.25, Phase = BeamPresentationPhase.Spawn, RootDirection = Vector2.right,
            Element = AttackElement.Poison, AnimationDuration = 1.5f,
            Stats = new ProjectilePresentationStats
            {
                DamageMultiplierSum = 0.3f, SpeedMultiplierSum = 0.5f, SizeMultiplierSum = 0.2f,
                DurationMultiplierSum = 1.1f, EffectiveSpeed = 0, Duration = 1.5f,
                ProjectileCount = 3, BaseProjectileCount = 1
            }
        };

        private static NetworkBeamPresentationEdge End(ulong root = Root, ushort index = 0) => new NetworkBeamPresentationEdge
        {
            SourcePlayerId = Source, WeaponId = Weapon, AttackEventId = root, BeamIndex = index,
            EventNetworkTime = 6.75, Phase = BeamPresentationPhase.Terminated
        };

        private static NetworkBeamPresentationAim Aim(ulong root = Root) => new NetworkBeamPresentationAim
        {
            SourcePlayerId = Source, WeaponId = Weapon, AttackEventId = root,
            EventNetworkTime = 5.5, RootDirection = Vector2.up
        };
    }
}
