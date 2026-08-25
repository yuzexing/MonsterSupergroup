using System.Collections.Generic;
using NUnit.Framework;

namespace MonsterSupergroup.GAS.Tests
{
    public sealed class StatusReplicationTests
    {
        private static readonly StatusDefinition Poison =
            new StatusDefinition(EnemyStatusID.Poison, StatusStackMode.Add, 20);

        [Test]
        public void EffectiveRegistry_DistinguishesSameDefinitionFromDifferentPlayers()
        {
            var controller = new StatusController(_ => { });
            controller.Apply(Application(
                new StatusInstanceId(101),
                sourcePlayerId: 1,
                stack: 1));
            controller.Apply(Application(
                new StatusInstanceId(102),
                sourcePlayerId: 2,
                stack: 1));

            IReadOnlyList<StatusInstance> poison = controller.GetInstances(EnemyStatusID.Poison);

            Assert.That(poison, Has.Count.EqualTo(2));
            Assert.That(poison[0].InstanceId, Is.Not.EqualTo(poison[1].InstanceId));
            Assert.That(controller.HasFromSource(EnemyStatusID.Poison, 1), Is.True);
            Assert.That(controller.HasFromSource(EnemyStatusID.Poison, 2), Is.True);
            Assert.That(controller.GetStackCount(EnemyStatusID.Poison), Is.EqualTo(2));
        }

        [Test]
        public void CanonicalAndPredictedStacks_ReconcileWithoutWaitingToQueryThreshold()
        {
            var controller = new StatusController(_ => { });
            StatusInstance canonicalSeven = Instance(
                new StatusInstanceId(201),
                sourcePlayerId: 1,
                stack: 7,
                version: 1,
                authority: StatusExecutionAuthority.SourceClient);
            Assert.That(controller.UpsertCanonical(canonicalSeven), Is.True);

            Assert.That(
                controller.ApplyPredictedStackDelta(canonicalSeven.InstanceId, 3),
                Is.True);
            Assert.That(controller.GetCanonicalStackCount(EnemyStatusID.Poison), Is.EqualTo(7));
            Assert.That(controller.GetPredictedStackDelta(EnemyStatusID.Poison), Is.EqualTo(3));
            Assert.That(controller.GetStackCount(EnemyStatusID.Poison), Is.EqualTo(10));

            StatusInstance canonicalTen = Instance(
                canonicalSeven.InstanceId,
                sourcePlayerId: 1,
                stack: 10,
                version: 2,
                authority: StatusExecutionAuthority.SourceClient);
            Assert.That(controller.UpsertCanonical(canonicalTen), Is.True);
            Assert.That(controller.GetCanonicalStackCount(EnemyStatusID.Poison), Is.EqualTo(10));
            Assert.That(controller.GetPredictedStackDelta(EnemyStatusID.Poison), Is.Zero);
            Assert.That(controller.GetStackCount(EnemyStatusID.Poison), Is.EqualTo(10));

            Assert.That(controller.UpsertCanonical(canonicalSeven), Is.False);
            Assert.That(controller.GetStackCount(EnemyStatusID.Poison), Is.EqualTo(10));
        }

        [Test]
        public void SourceClientStatus_IsQueryableEverywhereButTicksOnOneExecutor()
        {
            StatusInstance replica = Instance(
                new StatusInstanceId(301),
                sourcePlayerId: 1,
                stack: 1,
                version: 1,
                authority: StatusExecutionAuthority.SourceClient);
            var aTicks = new List<StatusTick>();
            var bTicks = new List<StatusTick>();
            var serverTicks = new List<StatusTick>();
            var observerTicks = new List<StatusTick>();
            StatusController a = Controller(aTicks, isServer: false, localPlayerId: 1);
            StatusController b = Controller(bTicks, isServer: false, localPlayerId: 2);
            StatusController server = Controller(serverTicks, isServer: true, localPlayerId: 0);
            StatusController observer = Controller(observerTicks, isServer: false, localPlayerId: 3);

            StatusController[] machines = { a, b, server, observer };
            for (int i = 0; i < machines.Length; i++)
            {
                machines[i].UpsertCanonical(replica);
                Assert.That(machines[i].Has(EnemyStatusID.Poison), Is.True);
                machines[i].Advance(1f);
            }

            Assert.That(aTicks, Has.Count.EqualTo(1));
            Assert.That(bTicks, Is.Empty);
            Assert.That(serverTicks, Is.Empty);
            Assert.That(observerTicks, Is.Empty);
        }

        [Test]
        public void SourceDisconnect_ServerAuthorityContinuesOnlyRemainingTicks()
        {
            var sourceTicks = new List<StatusTick>();
            var serverTicks = new List<StatusTick>();
            StatusController source = Controller(sourceTicks, isServer: false, localPlayerId: 1);
            StatusController server = Controller(serverTicks, isServer: true, localPlayerId: 0);
            StatusInstance sourceOwned = Instance(
                new StatusInstanceId(401),
                sourcePlayerId: 1,
                stack: 1,
                version: 1,
                authority: StatusExecutionAuthority.SourceClient,
                totalTicks: 3,
                completedTicks: 0);
            source.UpsertCanonical(sourceOwned);
            server.UpsertCanonical(sourceOwned);

            source.Advance(1f);
            server.Advance(1f);
            Assert.That(sourceTicks, Has.Count.EqualTo(1));
            Assert.That(serverTicks, Is.Empty);

            StatusInstance failover = Instance(
                sourceOwned.InstanceId,
                sourcePlayerId: 1,
                stack: 1,
                version: 2,
                authority: StatusExecutionAuthority.Server,
                totalTicks: 3,
                completedTicks: 1);
            server.UpsertCanonical(failover);
            server.Advance(2f);

            Assert.That(serverTicks, Has.Count.EqualTo(2));
            Assert.That(serverTicks[0].TickIndex, Is.EqualTo(2));
            Assert.That(serverTicks[1].TickIndex, Is.EqualTo(3));
            Assert.That(serverTicks[1].IsFinalTick, Is.True);
        }

        [Test]
        public void ServerAuthorityStatus_DoesNotExecuteOnObservingClients()
        {
            StatusInstance stun = new StatusInstance(
                new StatusInstanceId(501),
                new StatusDefinition(EnemyStatusID.Stun, StatusStackMode.Replace, 1),
                sourcePlayerId: 1,
                sourceEntityId: 10,
                targetEntityId: 99,
                stack: 1,
                startTime: 0,
                duration: 1,
                executionAuthority: StatusExecutionAuthority.Server,
                version: 1,
                tickDamage: 0,
                totalTicks: 1,
                completedTicks: 0,
                tickInterval: 1,
                priority: 1,
                damageSourceId: 10);
            var serverTicks = new List<StatusTick>();
            var clientTicks = new List<StatusTick>();
            StatusController server = Controller(serverTicks, true, 0);
            StatusController client = Controller(clientTicks, false, 1);

            server.UpsertCanonical(stun);
            client.UpsertCanonical(stun);
            server.Advance(1f);
            client.Advance(1f);

            Assert.That(serverTicks, Has.Count.EqualTo(1));
            Assert.That(clientTicks, Is.Empty);
        }

        private static StatusController Controller(
            ICollection<StatusTick> ticks,
            bool isServer,
            uint localPlayerId)
        {
            return new StatusController(
                ticks.Add,
                new SequentialStatusInstanceIdSource(),
                new StatusExecutionScope(false, isServer, localPlayerId));
        }

        private static StatusApplication Application(
            StatusInstanceId instanceId,
            uint sourcePlayerId,
            int stack)
        {
            return new StatusApplication(
                Poison,
                tickDamage: 2,
                numberOfHits: 3,
                hitIntervalDuration: 1,
                priority: 1,
                damageSourceId: sourcePlayerId,
                instanceId: instanceId,
                stack: stack,
                sourcePlayerId: sourcePlayerId,
                sourceEntityId: sourcePlayerId * 10,
                targetEntityId: 99,
                executionAuthority: StatusExecutionAuthority.SourceClient);
        }

        private static StatusInstance Instance(
            StatusInstanceId instanceId,
            uint sourcePlayerId,
            int stack,
            uint version,
            StatusExecutionAuthority authority,
            int totalTicks = 3,
            int completedTicks = 0)
        {
            return new StatusInstance(
                instanceId,
                Poison,
                sourcePlayerId,
                sourcePlayerId * 10,
                targetEntityId: 99,
                stack: stack,
                startTime: 0,
                duration: totalTicks,
                executionAuthority: authority,
                version: version,
                tickDamage: 2,
                totalTicks: totalTicks,
                completedTicks: completedTicks,
                tickInterval: 1,
                priority: 1,
                damageSourceId: sourcePlayerId);
        }
    }
}
