using System.Collections.Generic;
using MonsterSupergroup.GAS;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class NetworkCombatGatewayTests
    {
        [Test]
        public void OwnerPipeline_SubmitsRawOverkillAndServerConvergesCanonicalHp()
        {
            var ids = new SequentialCombatEventIdSource(1, 1);
            var collector = new ClientCombatCollector(1, ids);
            var pipeline = new CombatPipeline(
                new RuntimeEquipmentModifiers(),
                new FixedRandom(),
                ids,
                collector);
            var predictedTarget = new Target(100, health: 30, version: 7);
            AttackSnapshot attack = pipeline.BeginAttack(new Weapon(1, 10, damage: 100));

            CombatResolution local = pipeline.ResolveHitDetailed(attack, predictedTarget);

            Assert.That(local.ResolvedDamage.Value, Is.EqualTo(100));
            Assert.That(local.PredictedAppliedDamage.Value, Is.EqualTo(30));
            Assert.That(local.IsPredictedLethal, Is.True);
            Assert.That(predictedTarget.Health, Is.Zero, "Owner feedback is immediate.");

            var gateway = Gateway(enemyHealth: 30);
            CanonicalWorldBatch canonical = gateway.ProcessBatch(
                1,
                collector.Drain(1),
                serverTime: 0);

            Assert.That(canonical.Entities, Has.Length.EqualTo(1));
            Assert.That(canonical.Entities[0].Health, Is.Zero);
            Assert.That(canonical.Entities[0].Alive, Is.False);
            Assert.That(canonical.ConfirmedKills, Has.Length.EqualTo(1));
        }

        [Test]
        public void TwoClients_StaleObservedVersionIsAcceptedButConfirmedKillOccursOnce()
        {
            var gateway = new ServerCombatGateway();
            gateway.Ledger.RegisterSource(10, 1);
            gateway.Ledger.RegisterSource(20, 2);
            gateway.Ledger.RegisterEntity(
                100,
                100,
                CombatEntityKind.Enemy,
                CombatEntityAuthority.ServerCanonical);

            CanonicalWorldBatch first = gateway.ProcessBatch(
                1,
                Batch(1, Result(1, 10, 100, 70, targetVersion: 1)),
                0);
            CanonicalWorldBatch second = gateway.ProcessBatch(
                2,
                Batch(1, Result(2, 20, 100, 60, targetVersion: 1)),
                0);
            CanonicalWorldBatch duplicateLethal = gateway.ProcessBatch(
                1,
                Batch(2, Result(3, 10, 100, 999, targetVersion: 1)),
                0);

            Assert.That(first.Entities[0].Health, Is.EqualTo(30));
            Assert.That(second.Entities[0].Health, Is.Zero);
            Assert.That(second.ConfirmedKills, Has.Length.EqualTo(1));
            Assert.That(duplicateLethal.ConfirmedKills, Is.Empty);
            Assert.That(gateway.Metrics.ConfirmedKills, Is.EqualTo(1));
            Assert.That(
                gateway.Metrics.GetRejected(CombatRejectionReason.TargetCanonicalDead),
                Is.EqualTo(1));
        }

        [Test]
        public void Server_UsesSubmittedDamageAndTagsWithoutRecomputingClientBuild()
        {
            var gateway = Gateway(enemyHealth: 500);
            CombatResult result = Result(1, 10, 100, 273, targetVersion: 51);
            result.DamageTags = (ulong)(
                CombatTags.Projectile |
                CombatTags.Explosion |
                CombatTags.Fire |
                CombatTags.Critical);

            CanonicalWorldBatch canonical = gateway.ProcessBatch(1, Batch(1, result), 0);

            Assert.That(canonical.Entities[0].Health, Is.EqualTo(227));
            Assert.That(gateway.Metrics.AcceptedCombatResults, Is.EqualTo(1));
        }

        [Test]
        public void StatusMutation_BecomesGlobalReplicaAndOnlySourceClientExecutes()
        {
            var gateway = Gateway(enemyHealth: 100);
            StatusMutation poison = Mutation(
                eventId: 10,
                instanceId: 900,
                sourcePlayerId: 1,
                sourceEntityId: 10,
                targetEntityId: 100,
                stackDelta: 3);
            CanonicalWorldBatch canonical = gateway.ProcessBatch(
                1,
                new CombatSubmissionBatch
                {
                    BatchSequence = 1,
                    Results = System.Array.Empty<CombatResult>(),
                    StatusMutations = new[] { poison },
                    PlayerHealthReports = System.Array.Empty<PlayerHealthReport>()
                },
                0);
            var bTicks = new List<StatusTick>();
            var bStatuses = new StatusController(
                bTicks.Add,
                new SequentialStatusInstanceIdSource(),
                new StatusExecutionScope(false, false, localPlayerId: 2));
            var replica = new CanonicalWorldReplica();
            replica.RegisterStatusController(100, bStatuses);

            replica.Apply(canonical);
            bStatuses.Advance(1f);

            Assert.That(replica.HasStatus(100, EnemyStatusID.Poison), Is.True);
            Assert.That(bStatuses.GetStackCount(EnemyStatusID.Poison), Is.EqualTo(3));
            Assert.That(bStatuses.HasFromSource(EnemyStatusID.Poison, 1), Is.True);
            Assert.That(bTicks, Is.Empty);
        }

        [Test]
        public void SourceDisconnect_ServerContinuesRemainingDotWithoutBuildRerun()
        {
            var gateway = Gateway(enemyHealth: 100);
            gateway.ProcessBatch(
                1,
                new CombatSubmissionBatch
                {
                    BatchSequence = 1,
                    Results = System.Array.Empty<CombatResult>(),
                    StatusMutations = new[]
                    {
                        Mutation(10, 901, 1, 10, 100, 1, totalTicks: 3, tickDamage: 10)
                    },
                    PlayerHealthReports = System.Array.Empty<PlayerHealthReport>()
                },
                0);

            CanonicalWorldBatch failover = gateway.HandleSourceDisconnected(1, serverTime: 1);
            CanonicalWorldBatch remaining = gateway.Advance(serverTime: 3);

            Assert.That(failover.Statuses, Has.Length.EqualTo(1));
            Assert.That(
                failover.Statuses[0].ExecutionAuthority,
                Is.EqualTo((byte)StatusExecutionAuthority.Server));
            Assert.That(remaining.Entities, Has.Length.EqualTo(1));
            Assert.That(remaining.Entities[0].Health, Is.EqualTo(80));
            Assert.That(remaining.Statuses, Has.Length.EqualTo(1));
            Assert.That(remaining.Statuses[0].Removed, Is.True);
        }

        [Test]
        public void PlayerHealth_IsOwnerFinalAndCannotBeWrittenByAnotherClient()
        {
            var gateway = new ServerCombatGateway();
            gateway.Ledger.RegisterEntity(
                200,
                100,
                CombatEntityKind.Player,
                CombatEntityAuthority.OwnerFinal,
                ownerPlayerId: 1);
            var report = new PlayerHealthReport
            {
                EventId = 1,
                Sequence = 1,
                PlayerId = 1,
                EntityId = 200,
                Health = 40,
                MaxHealth = 100,
                Alive = true,
                StateVersion = 2
            };

            CanonicalWorldBatch rejected = gateway.ProcessBatch(
                2,
                PlayerBatch(1, report),
                0);
            CanonicalWorldBatch accepted = gateway.ProcessBatch(
                1,
                PlayerBatch(2, report),
                0);

            Assert.That(rejected.Entities, Is.Empty);
            Assert.That(accepted.Entities[0].Health, Is.EqualTo(40));
        }

        [Test]
        public void DuplicateEvent_IsSettledExactlyOnceEvenWhenBatchIsRetransmitted()
        {
            var gateway = Gateway(enemyHealth: 100);
            CombatResult hit = Result(44, 10, 100, 25, targetVersion: 1);

            gateway.ProcessBatch(1, Batch(1, hit), 0);
            CanonicalWorldBatch duplicate = gateway.ProcessBatch(1, Batch(1, hit), 0.1);

            gateway.Ledger.TryGetState(100, out CanonicalEntityState state);
            Assert.That(state.Health, Is.EqualTo(75));
            Assert.That(duplicate.Entities, Is.Empty);
            Assert.That(
                gateway.Metrics.GetRejected(CombatRejectionReason.DuplicateEvent),
                Is.EqualTo(1));
        }

        [Test]
        public void RegisteredConnectionIdentity_RejectsWrongEpochButAllowsReorderedBatches()
        {
            var gateway = Gateway(enemyHealth: 100);
            gateway.RegisterClientIdentity(1, sourceSlot: 5, connectionEpoch: 8);
            CombatEventId validId = CombatEventId.Compose(5, 8, 10);
            CombatResult valid = Result(validId.Value, 10, 100, 10, 1);
            valid.Sequence = validId.Sequence;
            CombatEventId wrongId = CombatEventId.Compose(5, 7, 11);
            CombatResult wrong = Result(wrongId.Value, 10, 100, 10, 1);
            wrong.Sequence = wrongId.Sequence;

            gateway.ProcessBatch(1, Batch(10, valid), 0);
            gateway.ProcessBatch(1, Batch(9, wrong), 0);

            gateway.Ledger.TryGetState(100, out CanonicalEntityState state);
            Assert.That(state.Health, Is.EqualTo(90));
            Assert.That(
                gateway.Metrics.GetRejected(CombatRejectionReason.InvalidSequence),
                Is.EqualTo(1));
        }

        [Test]
        public void Batch_PreservesEveryEventOrderAndTraceInsteadOfMergingDamage()
        {
            var trace = new CombatTraceRecorder(16);
            var gateway = new ServerCombatGateway(trace: trace);
            gateway.Ledger.RegisterSource(10, 1);
            gateway.Ledger.RegisterEntity(
                100,
                100,
                CombatEntityKind.Enemy,
                CombatEntityAuthority.ServerCanonical);
            CombatResult first = Result(1, 10, 100, 50, 1);
            CombatResult second = Result(2, 10, 100, 20, 1);
            CombatResult third = Result(3, 10, 100, 30, 1);

            CanonicalWorldBatch canonical = gateway.ProcessBatch(
                1,
                new CombatSubmissionBatch
                {
                    BatchSequence = 1,
                    Results = new[] { first, second, third },
                    StatusMutations = System.Array.Empty<StatusMutation>(),
                    PlayerHealthReports = System.Array.Empty<PlayerHealthReport>()
                },
                0);

            Assert.That(gateway.Metrics.AcceptedCombatResults, Is.EqualTo(3));
            Assert.That(canonical.Entities[0].Health, Is.Zero);
            CombatTraceEntry[] entries = trace.Snapshot();
            Assert.That(entries, Has.Length.EqualTo(4));
            Assert.That(entries[0].Damage, Is.EqualTo(50));
            Assert.That(entries[1].Damage, Is.EqualTo(20));
            Assert.That(entries[2].Damage, Is.EqualTo(30));
            Assert.That(entries[3].Kind, Is.EqualTo(CombatTraceKind.ConfirmedKill));
        }

        [Test]
        public void ProcessedEventCache_IsBoundedAndExpiresOldIds()
        {
            var cache = new ProcessedEventCache(capacity: 2, retentionSeconds: 5);
            cache.MarkProcessed(1, 0);
            cache.MarkProcessed(2, 0);
            cache.MarkProcessed(3, 0);

            Assert.That(cache.Count, Is.EqualTo(2));
            Assert.That(cache.IsProcessed(1, 0), Is.False);
            Assert.That(cache.IsProcessed(2, 0), Is.True);
            Assert.That(cache.IsProcessed(2, 6), Is.False);
            Assert.That(cache.Count, Is.Zero);
        }

        [Test]
        public void CombatTraceRing_KeepsLatestEntriesInChronologicalOrder()
        {
            var trace = new CombatTraceRecorder(capacity: 3);
            var ids = new SequentialCombatEventIdSource();
            CombatContext root = CombatContext.CreateRoot(ids.Next(), 1, 10, 77);
            for (int i = 1; i <= 4; i++)
            {
                CombatContext damage = root.CreateChild(ids.Next(), CombatTags.Damage, 100);
                trace.RecordResolvedDamage(damage, i);
            }

            CombatTraceEntry[] entries = trace.Snapshot();
            Assert.That(entries, Has.Length.EqualTo(3));
            Assert.That(entries[0].Damage, Is.EqualTo(2));
            Assert.That(entries[1].Damage, Is.EqualTo(3));
            Assert.That(entries[2].Damage, Is.EqualTo(4));
        }

        private static ServerCombatGateway Gateway(int enemyHealth)
        {
            var gateway = new ServerCombatGateway();
            gateway.Ledger.RegisterSource(10, 1);
            gateway.Ledger.RegisterEntity(
                100,
                enemyHealth,
                CombatEntityKind.Enemy,
                CombatEntityAuthority.ServerCanonical);
            return gateway;
        }

        private static CombatResult Result(
            ulong eventId,
            uint sourceEntityId,
            uint targetEntityId,
            int damage,
            uint targetVersion)
        {
            return new CombatResult
            {
                EventId = eventId,
                RootEventId = eventId,
                Sequence = (uint)eventId,
                SourcePlayerId = sourceEntityId == 20 ? 2u : 1u,
                SourceEntityId = sourceEntityId,
                TargetEntityId = targetEntityId,
                AbilityId = 77,
                Damage = damage,
                DamageTags = (ulong)CombatTags.Damage,
                TargetStateVersion = targetVersion
            };
        }

        private static CombatSubmissionBatch Batch(uint sequence, CombatResult result)
        {
            return new CombatSubmissionBatch
            {
                BatchSequence = sequence,
                Results = new[] { result },
                StatusMutations = System.Array.Empty<StatusMutation>(),
                PlayerHealthReports = System.Array.Empty<PlayerHealthReport>()
            };
        }

        private static CombatSubmissionBatch PlayerBatch(uint sequence, PlayerHealthReport report)
        {
            return new CombatSubmissionBatch
            {
                BatchSequence = sequence,
                Results = System.Array.Empty<CombatResult>(),
                StatusMutations = System.Array.Empty<StatusMutation>(),
                PlayerHealthReports = new[] { report }
            };
        }

        private static StatusMutation Mutation(
            ulong eventId,
            ulong instanceId,
            uint sourcePlayerId,
            uint sourceEntityId,
            uint targetEntityId,
            int stackDelta,
            int totalTicks = 3,
            int tickDamage = 2)
        {
            return new StatusMutation
            {
                EventId = eventId,
                RootEventId = eventId,
                Sequence = (uint)eventId,
                Kind = StatusMutationKind.ApplyOrRefresh,
                InstanceId = instanceId,
                DefinitionId = (uint)EnemyStatusID.Poison,
                StackMode = (byte)StatusStackMode.Add,
                MaxStacks = 20,
                SourcePlayerId = sourcePlayerId,
                SourceEntityId = sourceEntityId,
                TargetEntityId = targetEntityId,
                StackDelta = stackDelta,
                StartTime = 0,
                Duration = totalTicks,
                ExecutionAuthority = (byte)StatusExecutionAuthority.SourceClient,
                TickDamage = tickDamage,
                TotalTicks = totalTicks,
                CompletedTicks = 0,
                TickInterval = 1,
                Priority = 1,
                DamageSourceId = sourceEntityId
            };
        }

        private sealed class FixedRandom : IRandomSource
        {
            public float Next01() => 0f;
        }

        private sealed class Weapon : IWeaponRuntime, ICombatContextSource
        {
            public Weapon(uint playerId, uint entityId, int damage)
            {
                SourcePlayerId = playerId;
                SourceEntityId = entityId;
                CombatId = 77;
                Stats = new WeaponBehaviourStats(new AttackStats
                {
                    damage = damage,
                    speed = 1,
                    size = 1,
                    duration = 1,
                    projectileCount = 1,
                    critMultiplier = 1
                });
            }

            public uint CombatId { get; }
            public WeaponBehaviourStats Stats { get; }
            public uint SourcePlayerId { get; }
            public uint SourceEntityId { get; }
        }

        private sealed class Target : ICombatTarget, ICombatStateIdentity
        {
            public Target(uint entityId, int health, uint version)
            {
                EntityId = entityId;
                Health = health;
                StateVersion = version;
            }

            public uint EntityId { get; }
            public uint StateVersion { get; }
            public int Health { get; private set; }
            public bool IsAlive => Health > 0;

            public DamageInfo ReceiveDamage(DamageInfo requestedDamage)
            {
                int applied = System.Math.Min(Health, requestedDamage.Value);
                Health -= applied;
                return new DamageInfo(requestedDamage.Id, applied, requestedDamage.IsCritical);
            }

            public StatusApplicationResult ApplyStatus(StatusApplication application) =>
                StatusApplicationResult.Added;
        }
    }
}
