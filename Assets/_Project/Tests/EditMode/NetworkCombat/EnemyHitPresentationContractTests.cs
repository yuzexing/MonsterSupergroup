using System;
using System.Linq;
using Mirror;
using MonsterSupergroup.GAS;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class EnemyHitPresentationContractTests
    {
        [Test]
        public void EachAcceptedDamageKeepsItsIdentityAndVersionWhenHealthIsMerged()
        {
            var gateway = Gateway();
            var batch = gateway.ProcessBatch(1, new CombatSubmissionBatch
            {
                BatchSequence = 1,
                Results = new[] { Hit(1), Hit(2), Hit(3) }
            }, 0);
            Assert.That(batch.Entities, Has.Length.EqualTo(1));
            Assert.That(batch.Entities[0].Health, Is.EqualTo(70));
            Assert.That(batch.EnemyHitPresentations.Select(h => h.DamageEventId), Is.EqualTo(new ulong[] { 1, 2, 3 }));
            Assert.That(batch.EnemyHitPresentations.Select(h => h.TargetStateVersion), Is.EqualTo(new uint[] { 2, 3, 4 }));
            Assert.That(batch.EnemyHitPresentations.All(h => h.TargetEntityId == 100), Is.True);
            Assert.That(gateway.CreateSnapshot().EnemyHitPresentations, Is.Empty);
            Assert.That(gateway.CreateEntityUpdate(batch.Entities[0]).EnemyHitPresentations, Is.Empty);
        }

        [Test]
        public void DuplicateRejectedZeroAndDeadTargetResultsNeverCreateAnotherFlash()
        {
            var gateway = Gateway();
            var lethal = Hit(1); lethal.Damage = 100;
            Assert.That(gateway.ProcessBatch(1, Batch(1, lethal), 0).EnemyHitPresentations, Has.Length.EqualTo(1));
            Assert.That(gateway.ProcessBatch(1, Batch(2, lethal), .1).EnemyHitPresentations, Is.Empty);
            Assert.That(gateway.ProcessBatch(1, Batch(3, Hit(2)), .2).EnemyHitPresentations, Is.Empty);
            gateway = Gateway();
            var zero = Hit(3); zero.Damage = 0;
            Assert.That(gateway.ProcessBatch(1, Batch(1, zero), 0).EnemyHitPresentations, Is.Empty);
            var invalid = Hit(4); invalid.TargetEntityId = 999;
            Assert.That(gateway.ProcessBatch(1, Batch(2, invalid), 0).EnemyHitPresentations, Is.Empty);
            Assert.That(gateway.ProcessBatch(2, Batch(3, Hit(5)), 0).EnemyHitPresentations, Is.Empty);
            gateway.Ledger.SetAbsoluteInvulnerable(100, true);
            Assert.That(gateway.ProcessBatch(1, Batch(4, Hit(6)), 0).EnemyHitPresentations, Has.Length.EqualTo(1),
                "An enemy hit already resolved by the owner remains final after a server immunity change.");
        }

        [TestCase(EnemyStatusID.Burn)]
        [TestCase(EnemyStatusID.Poison)]
        [TestCase(EnemyStatusID.Bleed)]
        public void ClientPeriodicDamageHasTheSamePresentationContract(EnemyStatusID status)
        {
            var tags = CombatTags.Damage | CombatTags.Status | CombatTags.Periodic;
            if (status == EnemyStatusID.Burn) tags |= CombatTags.Burn | CombatTags.Fire;
            if (status == EnemyStatusID.Poison) tags |= CombatTags.Poison;
            var hit = Hit(1); hit.DamageTags = (ulong)tags;
            var batch = Gateway().ProcessBatch(1, Batch(1, hit), 0);
            Assert.That(batch.EnemyHitPresentations, Has.Length.EqualTo(1));
            Assert.That(batch.EnemyHitPresentations[0].DamageEventId, Is.EqualTo(hit.EventId));
        }

        [Test]
        public void ServerTakeoverDotProducesDistinctEdgesAndNoHistoricalSnapshotFlashes()
        {
            var gateway = Gateway();
            gateway.ProcessBatch(1, new CombatSubmissionBatch
            {
                BatchSequence = 1,
                StatusMutations = new[] { new StatusMutation
                {
                    ApplicationRevision = 1,
                    EventId = 10, RootEventId = 10, Sequence = 10,
                    Kind = StatusMutationKind.ApplyOrRefresh, InstanceId = 900,
                    DefinitionId = (uint)EnemyStatusID.Poison, StackMode = (byte)StatusStackMode.Add,
                    MaxStacks = 20, SourcePlayerId = 1, SourceEntityId = 10, TargetEntityId = 100,
                    StackDelta = 1, Duration = 3, ExecutionAuthority = (byte)StatusExecutionAuthority.SourceClient,
                    TickDamage = 10, TotalTicks = 3, TickInterval = 1, Priority = 1, DamageSourceId = 10
                } }
            }, 0);
            Assert.That(gateway.HandleSourceDisconnected(1, 1).EnemyHitPresentations, Is.Empty);
            var batch = gateway.Advance(3);
            Assert.That(batch.Entities.Single().Health, Is.EqualTo(80));
            Assert.That(batch.EnemyHitPresentations, Has.Length.EqualTo(2));
            Assert.That(batch.EnemyHitPresentations.Select(h => h.DamageEventId).Distinct().Count(), Is.EqualTo(2));
            Assert.That(gateway.Advance(4).EnemyHitPresentations, Is.Empty);
            Assert.That(gateway.CreateSnapshot().EnemyHitPresentations, Is.Empty);
        }

        [Test]
        public void CollectorNotifiesSynchronouslyAfterQueueingAndReleasesSubscriptions()
        {
            var ids = new SequentialCombatEventIdSource(1, 1);
            var collector = new ClientCombatCollector(1, ids);
            int calls = 0;
            collector.DamageResolved += damage =>
            {
                calls++;
                Assert.That(collector.PendingResultCount, Is.EqualTo(calls));
                Assert.That(damage.Context.TargetEntityId, Is.EqualTo(100));
            };
            collector.Publish(Damage(1, 10));
            // Resolved-but-not-locally-applied damage is still submitted. Presentation filters it separately.
            collector.Publish(Damage(2, 0));
            Assert.That(calls, Is.EqualTo(2));
            collector.Dispose();
            collector.Publish(Damage(3, 10));
            Assert.That(calls, Is.EqualTo(2));
        }

        [Test]
        public void MirrorRoundTripRetainsOrderedHitEdgesAndEmptyDefaults()
        {
            var original = Gateway().ProcessBatch(1, Batch(1, Hit(1)), 0);
            using var writer = NetworkWriterPool.Get();
            writer.Write(original);
            using var reader = NetworkReaderPool.Get(writer.ToArraySegment());
            var decoded = reader.Read<CanonicalWorldBatch>();
            Assert.That(decoded.EnemyHitPresentations, Has.Length.EqualTo(1));
            Assert.That(decoded.EnemyHitPresentations[0].DamageEventId, Is.EqualTo(1));
            Assert.That(decoded.EnemyHitPresentations[0].TargetEntityId, Is.EqualTo(100));
            Assert.That(decoded.EnemyHitPresentations[0].TargetStateVersion, Is.EqualTo(2));
        }

        private static ServerCombatGateway Gateway()
        {
            var gateway = new ServerCombatGateway();
            gateway.Ledger.RegisterSource(10, 1);
            gateway.Ledger.RegisterEntity(100, 100, CombatEntityKind.Enemy, CombatEntityAuthority.ServerCanonical);
            return gateway;
        }
        private static CombatResult Hit(ulong id) => new CombatResult
        {
            EventId = id, RootEventId = id, Sequence = (uint)id, SourcePlayerId = 1,
            SourceEntityId = 10, TargetEntityId = 100, AbilityId = 77,
            Damage = 10, DamageTags = (ulong)CombatTags.Damage, TargetStateVersion = 1
        };
        private static CombatSubmissionBatch Batch(uint sequence, CombatResult hit) =>
            new CombatSubmissionBatch { BatchSequence = sequence, Results = new[] { hit } };
        private static CombatEvent Damage(ulong id, int predicted) => new CombatEvent(
            CombatEventKind.DamageResolved,
            new CombatContext(new CombatEventId(id), new CombatEventId(id), CombatEventId.None,
                (uint)id, 0, 1, 10, 100, 77, 0, CombatTags.Damage, 1),
            new DamageInfo(10, 10, false), new DamageInfo(10, predicted, false));
    }
}
