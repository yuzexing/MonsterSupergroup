using System.Collections.Generic;
using AstralShift.HellMaiden.Player.Attacks;
using NUnit.Framework;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class MeleePresentationContractTests
    {
        [Test]
        public void History_RejectsSameSlashAcrossBatchesUntilItsAttackRetires()
        {
            var history = new MeleePresentationHistory();
            var firstSlash = new MeleePresentationKey(101UL, 0);
            Assert.That(history.TryRecord(firstSlash), Is.True);
            Assert.That(history.TryRecord(new MeleePresentationKey(101UL, 1)), Is.True);
            Assert.That(history.TryRecord(new MeleePresentationKey(102UL, 0)), Is.True);
            // No visual expiry callback removes history: even a later reliable batch cannot
            // replay a slash that has already finished while other parts of the root remain.
            Assert.That(history.TryRecord(firstSlash), Is.False);
            Assert.That(history.AttackCount, Is.EqualTo(2));
            history.RetireAttack(101UL);
            Assert.That(history.AttackCount, Is.EqualTo(1));
            Assert.That(history.TryRecord(new MeleePresentationKey(102UL, 0)), Is.False);
            history.Clear();
            Assert.That(history.AttackCount, Is.Zero);
            Assert.That(history.TryRecord(default), Is.False);
            Assert.That(history.AttackCount, Is.Zero);
        }

        [Test]
        public void Keys_DistinguishSlashesAndAttackRoots()
        {
            var keys = new HashSet<MeleePresentationKey>
            {
                new MeleePresentationKey(101UL, 0),
                new MeleePresentationKey(101UL, 1),
                new MeleePresentationKey(102UL, 0),
                new MeleePresentationKey(101UL, 0)
            };
            Assert.That(keys.Count, Is.EqualTo(3));
            Assert.That(default(MeleePresentationKey).IsValid, Is.False);
        }

        [Test]
        public void Batch_RejectsWrongOwnerReplayAndOversizedSubmission()
        {
            var batch = new NetworkMeleePresentationBatch
            {
                BatchSequence = 1u,
                Edges = new[] { ValidSpawn(), ValidTermination() }
            };
            Assert.That(Valid(batch), Is.True);
            Assert.That(NetworkWeaponCombatAdapter.IsValidMeleePresentationBatch(
                batch, 18u, 0u, 32), Is.False);
            Assert.That(NetworkWeaponCombatAdapter.IsValidMeleePresentationBatch(
                batch, 17u, 1u, 32), Is.False);
            Assert.That(NetworkWeaponCombatAdapter.IsValidMeleePresentationBatch(
                batch, 17u, 2u, 32), Is.False);
            Assert.That(NetworkWeaponCombatAdapter.IsValidMeleePresentationBatch(
                batch, 17u, 0u, 1), Is.False);

            batch.Edges[1].SourcePlayerId = 18u;
            Assert.That(Valid(batch), Is.False,
                "A valid first edge cannot conceal another player's termination.");
        }

        [Test]
        public void Batch_SequenceWrapsAndRejectsEmptyData()
        {
            var batch = new NetworkMeleePresentationBatch
            { BatchSequence = 1u, Edges = new[] { ValidSpawn() } };
            Assert.That(NetworkWeaponCombatAdapter.IsValidMeleePresentationBatch(
                batch, 17u, uint.MaxValue, 32), Is.True);
            batch.BatchSequence = 0u;
            Assert.That(Valid(batch), Is.False);
            batch.BatchSequence = 1u;
            batch.Edges = new NetworkMeleePresentationEdge[0];
            Assert.That(Valid(batch), Is.False);
            batch.Edges = null;
            Assert.That(Valid(batch), Is.False);
        }

        [TestCase(-1f, true)]
        [TestCase(0.25f, true)]
        [TestCase(0f, false)]
        [TestCase(-2f, false)]
        [TestCase(float.NaN, false)]
        [TestCase(float.PositiveInfinity, false)]
        public void Duration_UsesAuthoredClipOrPositiveOverride(float duration, bool expected)
        {
            NetworkMeleePresentationEdge edge = ValidSpawn();
            edge.AnimationDuration = duration;
            Assert.That(edge.IsValid, Is.EqualTo(expected));
        }

        [Test]
        public void Spawn_RejectsMalformedVisualDataAndUnknownPhase()
        {
            NetworkMeleePresentationEdge edge = ValidSpawn();
            edge.LocalPosition.z = float.NaN;
            Assert.That(edge.IsValid, Is.False);
            edge = ValidSpawn();
            edge.Direction = Vector2.zero;
            Assert.That(edge.IsValid, Is.False);
            edge.Direction = new Vector2(float.MaxValue, float.MaxValue);
            Assert.That(edge.IsValid, Is.False);
            edge.Direction = new Vector2(float.PositiveInfinity, 1f);
            Assert.That(edge.IsValid, Is.False);
            edge = ValidSpawn();
            edge.Stats.ProjectileCount = 0;
            Assert.That(edge.IsValid, Is.False);
            edge = ValidSpawn();
            edge.EventNetworkTime = double.NaN;
            Assert.That(edge.IsValid, Is.False);
            edge = ValidSpawn();
            edge.Phase = (MeleePresentationPhase)255;
            Assert.That(edge.IsValid, Is.False);
        }

        [Test]
        public void Termination_RequiresIdentityButNoSpawnStats()
        {
            NetworkMeleePresentationEdge edge = ValidTermination();
            Assert.That(edge.Stats.ProjectileCount, Is.Zero);
            Assert.That(edge.IsValid, Is.True);
            Assert.That(edge.ToTermination().Key, Is.EqualTo(ValidSpawn().Key));
            edge.AttackEventId = 0UL;
            Assert.That(edge.IsValid, Is.False);
            edge = ValidTermination();
            edge.WeaponId = 0u;
            Assert.That(edge.IsValid, Is.False);
            edge = ValidTermination();
            edge.SourcePlayerId = 0u;
            Assert.That(edge.IsValid, Is.False);
        }

        [Test]
        public void Spawn_PreservesLocalOffsetSnapshotAndDuration()
        {
            NetworkMeleePresentationEdge edge = ValidSpawn();
            edge.Direction = new Vector2(3f, 4f);
            edge.AnimationDuration = 0.3f;
            MeleePresentationSpawn spawn = edge.ToSpawn();
            Assert.That(spawn.WeaponId, Is.EqualTo(1u));
            Assert.That(spawn.Key, Is.EqualTo(edge.Key));
            Assert.That(spawn.LocalPosition, Is.EqualTo(edge.LocalPosition));
            Assert.That(spawn.Direction, Is.EqualTo(new Vector2(0.6f, 0.8f)));
            Assert.That(spawn.AnimationDuration, Is.EqualTo(0.3f));
            Assert.That(spawn.Stats.SizeMultiplierSum, Is.EqualTo(0.5f));
            Assert.That(spawn.Stats.ProjectileCount, Is.EqualTo(3));
        }

        private static bool Valid(NetworkMeleePresentationBatch batch) =>
            NetworkWeaponCombatAdapter.IsValidMeleePresentationBatch(batch, 17u, 0u, 32);

        private static NetworkMeleePresentationEdge ValidSpawn() => new NetworkMeleePresentationEdge
        {
            SourcePlayerId = 17u,
            WeaponId = 1u,
            AttackEventId = 101UL,
            SlashIndex = 0,
            EventNetworkTime = 5d,
            Phase = MeleePresentationPhase.Spawn,
            LocalPosition = new Vector3(1f, 2f, 0f),
            Direction = Vector2.right,
            AnimationDuration = -1f,
            Stats = new ProjectilePresentationStats
            {
                SizeMultiplierSum = 0.5f,
                Duration = 1f,
                ProjectileCount = 3,
                BaseProjectileCount = 1
            }
        };

        private static NetworkMeleePresentationEdge ValidTermination() => new NetworkMeleePresentationEdge
        {
            SourcePlayerId = 17u,
            WeaponId = 1u,
            AttackEventId = 101UL,
            SlashIndex = 0,
            EventNetworkTime = 5.5d,
            Phase = MeleePresentationPhase.Terminated
        };
    }
}
