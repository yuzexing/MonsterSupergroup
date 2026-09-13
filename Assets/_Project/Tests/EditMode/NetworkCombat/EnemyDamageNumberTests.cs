using System.Linq;
using Mirror;
using MonsterSupergroup.GAS;
using NUnit.Framework;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class EnemyDamageNumberTests
    {
        [Test]
        public void PredictionEchoAndRepeatedBatchSubmitOnlyOnceButFailedPredictionCanRecover()
        {
            var history = new EnemyDamageNumberHistory();
            var hit = Hit(1, 2);
            Assert.That(history.CanPresent(hit, false, 1, 0), Is.True);
            history.MarkPresented(hit.DamageEventId, 0);
            Assert.That(history.CanPresent(hit, true, 1, .1), Is.False);
            Assert.That(history.CanPresent(hit, true, 2, .2), Is.False);
            // Successful playback is the only operation that consumes the event ID.
            hit = Hit(2, 3);
            Assert.That(history.CanPresent(hit, false, 2, .3), Is.True);
            Assert.That(history.CanPresent(hit, true, 2, .4), Is.True);
            Assert.That(history.CanPresent(hit, true, 3, .5), Is.True);
            history.MarkPresented(hit.DamageEventId, .5);
            Assert.That(history.CanPresent(hit, true, 3, .6), Is.False);
        }

        [Test]
        public void OrderedHitsSurviveCoalescingAndOldVersionsDoNotReplay()
        {
            var history = new EnemyDamageNumberHistory();
            foreach (var hit in new[] { Hit(1, 2), Hit(2, 3), Hit(3, 4) })
            {
                Assert.That(history.CanPresent(hit, true, 1, 0), Is.True);
                history.MarkPresented(hit.DamageEventId, 0);
            }
            Assert.That(history.CanPresent(Hit(99, 3), true, 1, 1), Is.False);
            Assert.That(history.CanPresent(Hit(99, 4), true, 1, 1), Is.False);
            Assert.That(history.CanPresent(Hit(4, 5), true, 6, 1), Is.False, "Newer snapshot masks old damage.");
            Assert.That(history.CanPresent(Hit(4, 7), true, 6, 1), Is.True);
        }

        [Test]
        public void DespawnKeepsReceiptUntilSessionResetAndHistoryExpires()
        {
            var history = new EnemyDamageNumberHistory();
            var hit = Hit(1, 2);
            history.MarkPresented(hit.DamageEventId, 0);
            Assert.That(history.CanPresent(hit, true, 0, 1), Is.False, "Despawn removes the replica, not the popup receipt.");
            history.Clear();
            Assert.That(history.CanPresent(hit, true, 0, 2), Is.True, "New session can reuse netIds.");
            history.MarkPresented(hit.DamageEventId, 2);
            Assert.That(history.CanPresent(hit, true, 0, 123), Is.True);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void NonPositiveDamageIsNotPresented(int damage)
        {
            var hit = Hit(1, 2); hit.Damage = damage;
            Assert.That(new EnemyDamageNumberHistory().CanPresent(hit, false, 0, 0), Is.False);
        }

        [Test]
        public void OverkillAndStyleSurviveCollectorGatewayAndMirrorRoundTrip()
        {
            var context = new CombatContext(new CombatEventId(1), new CombatEventId(1), default,
                1, 0, 1, 10, 100, 6, 0, CombatTags.Damage, 1);
            var combatEvent = new CombatEvent(CombatEventKind.DamageResolved, context,
                new DamageInfo(77, 50, true), new DamageInfo(77, 8, true),
                presentationDamageType: DamageType.Lightning);
            var result = CombatResult.From(combatEvent);
            using (var writer = NetworkWriterPool.Get())
            {
                writer.Write(result);
                using var reader = NetworkReaderPool.Get(writer.ToArraySegment());
                var decoded = reader.Read<CombatResult>();
                Assert.That(decoded.Damage, Is.EqualTo(50));
                Assert.That(decoded.DamageSourceId, Is.EqualTo(77));
                Assert.That(decoded.PresentationDamageType, Is.EqualTo((byte)DamageType.Lightning));
                Assert.That(decoded.IsCritical, Is.True);
            }
            var gateway = new ServerCombatGateway();
            gateway.Ledger.RegisterSource(10, 1);
            gateway.Ledger.RegisterEntity(100, 8, CombatEntityKind.Enemy, CombatEntityAuthority.ServerCanonical);
            var batch = gateway.ProcessBatch(1, new CombatSubmissionBatch { BatchSequence = 1, Results = new[] { result } }, 0);
            Assert.That(batch.Entities.Single().Health, Is.Zero);
            var hit = batch.EnemyHitPresentations.Single();
            Assert.That(hit.Damage, Is.EqualTo(50));
            Assert.That(hit.SourcePlayerId, Is.EqualTo(1));
            hit.HasPosition = true; hit.Position = new Vector3(1, 2, 3);
            using (var writer = NetworkWriterPool.Get())
            {
                writer.Write(hit);
                using var reader = NetworkReaderPool.Get(writer.ToArraySegment());
                var decoded = reader.Read<EnemyHitPresentation>();
                Assert.That(decoded.Damage, Is.EqualTo(50));
                Assert.That(decoded.IsCritical, Is.True);
                Assert.That(decoded.PresentationDamageType, Is.EqualTo((byte)DamageType.Lightning));
                Assert.That(decoded.DamageSourceId, Is.EqualTo(77));
                Assert.That(decoded.HasPosition, Is.True);
                Assert.That(decoded.Position, Is.EqualTo(hit.Position));
            }
            Assert.That(gateway.CreateSnapshot().EnemyHitPresentations, Is.Empty);
        }

        [TestCase(EnemyStatusID.Burn, DamageType.Fire)]
        [TestCase(EnemyStatusID.Poison, DamageType.Poison)]
        [TestCase(EnemyStatusID.Bleed, DamageType.Bleed)]
        public void ServerDotUsesStatusStyleWithoutInheritingParentCritical(EnemyStatusID status, DamageType type)
        {
            var gateway = new ServerCombatGateway();
            gateway.Ledger.RegisterSource(10, 1);
            gateway.Ledger.RegisterEntity(100, 100, CombatEntityKind.Enemy, CombatEntityAuthority.ServerCanonical);
            gateway.ProcessBatch(1, new CombatSubmissionBatch
            {
                BatchSequence = 1, StatusMutations = new[] { new StatusMutation
                {
                    ApplicationRevision = 1, EventId = 10, RootEventId = 10, Sequence = 10,
                    Kind = StatusMutationKind.ApplyOrRefresh, InstanceId = 900, DefinitionId = (uint)status,
                    StackMode = (byte)StatusStackMode.Add, MaxStacks = 20,
                    SourcePlayerId = 1, SourceEntityId = 10, TargetEntityId = 100, StackDelta = 1,
                    Duration = 3, ExecutionAuthority = (byte)StatusExecutionAuthority.SourceClient,
                    TickDamage = 10, TotalTicks = 3, TickInterval = 1, Priority = 1, DamageSourceId = 77,
                    Tags = (ulong)(CombatTags.Fire | CombatTags.Critical)
                } }
            }, 0);
            gateway.HandleSourceDisconnected(1, 0);
            var hits = gateway.Advance(3).EnemyHitPresentations;
            Assert.That(hits, Has.Length.EqualTo(3));
            Assert.That(hits.All(h => h.Damage == 10 && h.DamageSourceId == 77 && h.SourcePlayerId == 1 &&
                !h.IsCritical && h.PresentationDamageType == (byte)type), Is.True);
        }

        private static EnemyHitPresentation Hit(ulong id, uint version) => new EnemyHitPresentation
        { DamageEventId = id, TargetEntityId = 100, TargetStateVersion = version, Damage = 10 };
    }
}
