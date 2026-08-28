using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.Local;
using MonsterSupergroup.GAS;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class NetworkCombatPhase7Tests
    {
        private const string PlayerPrefabPath =
            "Assets/_Project/Content/NetworkCombat/NetworkPlayer.prefab";
        private const string EnemyPrefabPath =
            "Assets/_Project/Content/NetworkCombat/NetworkEnemy.prefab";
        private const string WorldPrefabPath =
            "Assets/_Project/Content/NetworkCombat/NetworkCombatWorld.prefab";
        private const string ProjectilePrefabPath =
            "Assets/_Project/Content/LocalCombat/LocalProjectile.prefab";
        private const string SandboxScenePath =
            "Assets/_Project/Scenes/Development/NetworkCombatSandbox.unity";

        [Test]
        public void LateJoinSnapshot_RehydratesEntitiesAndStatusesRegisteredAfterReceipt()
        {
            ServerCombatGateway gateway = Gateway(100);
            gateway.ProcessBatch(
                1,
                StatusBatch(1, Mutation(1, 901, 1, 10, 100, 3)),
                0);

            CanonicalWorldBatch snapshot = gateway.CreateSnapshot();
            var replica = new CanonicalWorldReplica();
            replica.Apply(snapshot);
            var remoteStatuses = new StatusController(
                _ => { },
                new SequentialStatusInstanceIdSource(),
                new StatusExecutionScope(false, false, 2));

            replica.RegisterStatusController(100, remoteStatuses);

            Assert.That(replica.TryGetEntity(100, out CanonicalEntityState entity), Is.True);
            Assert.That(entity.Health, Is.EqualTo(100));
            Assert.That(remoteStatuses.GetStackCount(EnemyStatusID.Poison), Is.EqualTo(3));
            Assert.That(remoteStatuses.HasFromSource(EnemyStatusID.Poison, 1), Is.True);
            Assert.That(
                remoteStatuses.GetInstances(EnemyStatusID.Poison)[0].Magnitude,
                Is.EqualTo(0.25f));
        }

        [Test]
        public void TwoHundredMillisecondRttWithJitterAndLoss_LocalFeedbackIsImmediateAndServerConverges()
        {
            var ids = new SequentialCombatEventIdSource(1, 9);
            var collector = new ClientCombatCollector(1, ids);
            var pipeline = new CombatPipeline(
                new RuntimeEquipmentModifiers(),
                new FixedRandom(),
                ids,
                collector);
            var localTarget = new Target(100, 100, 1);
            CombatResolution local = pipeline.ResolveHitDetailed(
                pipeline.BeginAttack(new Weapon(1, 10, 25)),
                localTarget);
            var localStatuses = new StatusController(
                _ => { },
                new SequentialStatusInstanceIdSource(1, 9),
                new StatusExecutionScope(false, false, 1));
            collector.Observe(localStatuses);
            localStatuses.Apply(new StatusApplication(
                new StatusDefinition(EnemyStatusID.Poison, StatusStackMode.Add, 20),
                tickDamage: 1,
                numberOfHits: 3,
                hitIntervalDuration: 1f,
                priority: 1f,
                damageSourceId: 10,
                instanceId: CombatEventIdToStatusId(1, 9, 500),
                stack: 2,
                sourcePlayerId: 1,
                sourceEntityId: 10,
                targetEntityId: 100,
                startTime: 0,
                executionAuthority: StatusExecutionAuthority.SourceClient,
                sourceContext: local.DamageContext));
            CombatSubmissionBatch outgoing = collector.Drain(1);

            Assert.That(localTarget.Health, Is.EqualTo(75), "Owner damage must not wait for RTT.");
            Assert.That(localStatuses.Has(EnemyStatusID.Poison), Is.True);
            Assert.That(outgoing.ResultCount, Is.EqualTo(1));
            Assert.That(outgoing.StatusMutationCount, Is.EqualTo(1));

            ServerCombatGateway gateway = Gateway(100);
            gateway.RegisterClientIdentity(1, 1, 9);
            var link = new SimulatedReliableLink(outgoing);
            Assert.That(link.TryReceive(0.20, out _), Is.False,
                "The first packet is intentionally lost and awaiting reliable retry.");
            gateway.Ledger.TryGetState(100, out CanonicalEntityState before);
            Assert.That(before.Health, Is.EqualTo(100));

            Assert.That(link.TryReceive(0.50, out CombatSubmissionBatch delivered), Is.True);
            CanonicalWorldBatch canonical = gateway.ProcessBatch(1, delivered, 0.50);
            var remoteStatuses = new StatusController(
                _ => { },
                new SequentialStatusInstanceIdSource(),
                new StatusExecutionScope(false, false, 2));
            var replica = new CanonicalWorldReplica();
            replica.RegisterStatusController(100, remoteStatuses);
            replica.Apply(canonical);

            Assert.That(replica.TryGetEntity(100, out CanonicalEntityState converged), Is.True);
            Assert.That(converged.Health, Is.EqualTo(75));
            Assert.That(remoteStatuses.GetStackCount(EnemyStatusID.Poison), Is.EqualTo(2));
        }

        [Test]
        public void SimultaneousPredictedLethal_BothBuildTriggersRunButServerConfirmsOnce()
        {
            var idsA = new SequentialCombatEventIdSource(1, 1);
            var idsB = new SequentialCombatEventIdSource(2, 1);
            var collectorA = new ClientCombatCollector(1, idsA);
            var collectorB = new ClientCombatCollector(2, idsB);
            var modifiersA = new RuntimeEquipmentModifiers();
            var modifiersB = new RuntimeEquipmentModifiers();
            var triggerA = new CountingPredictedLethalModifier(7001);
            var triggerB = new CountingPredictedLethalModifier(7002);
            modifiersA.Add(triggerA);
            modifiersB.Add(triggerB);
            var pipelineA = new CombatPipeline(modifiersA, new FixedRandom(), idsA, collectorA);
            var pipelineB = new CombatPipeline(modifiersB, new FixedRandom(), idsB, collectorB);
            var predictedA = new Target(100, 100, 1);
            var predictedB = new Target(100, 100, 1);

            pipelineA.ResolveHitDetailed(
                pipelineA.BeginAttack(new Weapon(1, 10, 150)), predictedA);
            pipelineB.ResolveHitDetailed(
                pipelineB.BeginAttack(new Weapon(2, 20, 150)), predictedB);

            Assert.That(triggerA.InvocationCount, Is.EqualTo(1));
            Assert.That(triggerB.InvocationCount, Is.EqualTo(1));
            Assert.That(predictedA.IsAlive, Is.False);
            Assert.That(predictedB.IsAlive, Is.False);

            var gateway = new ServerCombatGateway();
            gateway.Ledger.RegisterSource(10, 1);
            gateway.Ledger.RegisterSource(20, 2);
            gateway.Ledger.RegisterEntity(
                100, 100, CombatEntityKind.Enemy, CombatEntityAuthority.ServerCanonical);
            gateway.RegisterClientIdentity(1, 1, 1);
            gateway.RegisterClientIdentity(2, 2, 1);
            CanonicalWorldBatch first = gateway.ProcessBatch(1, collectorA.Drain(1), 0);
            CanonicalWorldBatch second = gateway.ProcessBatch(2, collectorB.Drain(1), 0);

            Assert.That(first.ConfirmedKills.Length + second.ConfirmedKills.Length, Is.EqualTo(1));
            Assert.That(gateway.Metrics.ConfirmedKills, Is.EqualTo(1));
            Assert.That(triggerA.InvocationCount, Is.EqualTo(1), "No local build rollback.");
            Assert.That(triggerB.InvocationCount, Is.EqualTo(1), "Losing kill credit does not undo B's build.");
        }

        [Test]
        public void SourceClientDotTick_UsesExistingCollectorAndChangesCanonicalHpExactlyOnce()
        {
            var ids = new SequentialCombatEventIdSource(1, 3);
            var collector = new ClientCombatCollector(1, ids);
            var targetObject = new GameObject("Network DOT Target");
            try
            {
                CombatantBehaviour target = targetObject.AddComponent<CombatantBehaviour>();
                target.Initialize(100);
                target.ConfigureEntityId(100);
                target.ConfigureStatusExecution(new StatusExecutionScope(false, false, 1));
                target.ConfigureStatusCombatEvents(ids, collector);
                collector.Observe(target.StatusController);
                CombatContext source = CombatContext.CreateRoot(
                    ids.Next(),
                    sourcePlayerId: 1,
                    sourceEntityId: 10,
                    abilityId: 77,
                    tags: CombatTags.Projectile | CombatTags.Fire | CombatTags.Burn);
                target.ApplyStatus(new StatusApplication(
                    new StatusDefinition(EnemyStatusID.Burn, StatusStackMode.Add, 20),
                    tickDamage: 10,
                    numberOfHits: 1,
                    hitIntervalDuration: 1f,
                    priority: 1f,
                    damageSourceId: 10,
                    instanceId: CombatEventIdToStatusId(1, 3, 500),
                    stack: 1,
                    sourcePlayerId: 1,
                    sourceEntityId: 10,
                    targetEntityId: 100,
                    startTime: 0,
                    executionAuthority: StatusExecutionAuthority.SourceClient,
                    sourceContext: source));

                target.AdvanceStatuses(1f);
                CombatSubmissionBatch outgoing = collector.Drain(1);

                Assert.That(target.CurrentHealth, Is.EqualTo(90));
                Assert.That(outgoing.ResultCount, Is.EqualTo(1));
                Assert.That(outgoing.StatusMutationCount, Is.EqualTo(2));
                Assert.That(outgoing.Results[0].Damage, Is.EqualTo(10));
                Assert.That(
                    ((CombatTags)outgoing.Results[0].DamageTags & CombatTags.Periodic) != 0,
                    Is.True);

                ServerCombatGateway gateway = Gateway(100);
                gateway.RegisterClientIdentity(1, 1, 3);
                gateway.ProcessBatch(1, outgoing, 1);
                gateway.Ledger.TryGetState(100, out CanonicalEntityState canonical);

                Assert.That(canonical.Health, Is.EqualTo(90));
                Assert.That(gateway.Metrics.AcceptedCombatResults, Is.EqualTo(1));
                Assert.That(gateway.Statuses.Count, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(targetObject);
                collector.Dispose();
            }
        }

        [Test]
        public void FourClientsOneHundredFiftyEnemies_BatchesConvergeWithinBudgets()
        {
            const int players = 4;
            const int enemies = 150;
            const int roundsPerPlayer = 3;
            const int damagePerHit = 100;
            const int enemyHealth = players * roundsPerPlayer * damagePerHit;
            var gateway = new ServerCombatGateway();
            for (uint player = 1; player <= players; player++)
            {
                gateway.Ledger.RegisterSource(1000 + player, player);
                gateway.RegisterClientIdentity(player, (ushort)player, 1);
            }

            for (uint enemy = 1; enemy <= enemies; enemy++)
            {
                gateway.Ledger.RegisterEntity(
                    10000 + enemy,
                    enemyHealth,
                    CombatEntityKind.Enemy,
                    CombatEntityAuthority.ServerCanonical);
            }

            long managedBefore = GC.GetTotalMemory(true);
            var timer = Stopwatch.StartNew();
            for (uint player = 1; player <= players; player++)
            {
                var ids = new SequentialCombatEventIdSource((ushort)player, 1);
                var statuses = new StatusMutation[enemies];
                for (uint enemy = 1; enemy <= enemies; enemy++)
                {
                    CombatEventId eventId = ids.Next();
                    statuses[enemy - 1] = Mutation(
                        eventId.Value,
                        CombatEventId.Compose((ushort)player, 2, enemy).Value,
                        player,
                        1000 + player,
                        10000 + enemy,
                        1);
                    statuses[enemy - 1].Sequence = eventId.Sequence;
                }

                gateway.ProcessBatch(
                    player,
                    new CombatSubmissionBatch
                    {
                        BatchSequence = 1,
                        Results = Array.Empty<CombatResult>(),
                        StatusMutations = statuses,
                        PlayerHealthReports = Array.Empty<PlayerHealthReport>()
                    },
                    0);

                var results = new CombatResult[enemies * roundsPerPlayer];
                int index = 0;
                for (int round = 0; round < roundsPerPlayer; round++)
                {
                    for (uint enemy = 1; enemy <= enemies; enemy++)
                    {
                        CombatEventId eventId = ids.Next();
                        results[index++] = Result(
                            eventId,
                            player,
                            1000 + player,
                            10000 + enemy,
                            damagePerHit);
                    }
                }

                gateway.ProcessBatch(
                    player,
                    new CombatSubmissionBatch
                    {
                        BatchSequence = 2,
                        Results = results,
                        StatusMutations = Array.Empty<StatusMutation>(),
                        PlayerHealthReports = Array.Empty<PlayerHealthReport>()
                    },
                    0);
            }

            timer.Stop();
            long managedGrowth = Math.Max(0L, GC.GetTotalMemory(false) - managedBefore);
            int totalEvents = players * enemies * (1 + roundsPerPlayer);

            Assert.That(gateway.Metrics.ReceivedBatches, Is.EqualTo(players * 2));
            Assert.That(gateway.Metrics.AcceptedStatusMutations, Is.EqualTo(players * enemies));
            Assert.That(
                gateway.Metrics.AcceptedCombatResults,
                Is.EqualTo(players * enemies * roundsPerPlayer));
            Assert.That(gateway.Metrics.ConfirmedKills, Is.EqualTo(enemies));
            Assert.That(gateway.Statuses.Count, Is.Zero);
            Assert.That(gateway.ProcessedEvents.Count, Is.EqualTo(totalEvents));
            Assert.That(gateway.ProcessedEvents.Count, Is.LessThanOrEqualTo(
                gateway.ProcessedEvents.Capacity));
            Assert.That(gateway.Metrics.ReceivedBatches * 100, Is.LessThan(totalEvents),
                "Combat events must be batched, not sent one Command each.");
            Assert.That(timer.ElapsedMilliseconds, Is.LessThan(5000));
            Assert.That(managedGrowth, Is.LessThan(128L * 1024L * 1024L));
            Assert.That(gateway.Metrics.EstimatedReceivedPayloadBytes, Is.GreaterThan(0));

            for (uint enemy = 1; enemy <= enemies; enemy++)
            {
                Assert.That(
                    gateway.Ledger.TryGetState(10000 + enemy, out CanonicalEntityState state),
                    Is.True);
                Assert.That(state.Health, Is.Zero);
                Assert.That(state.Alive, Is.False);
            }

            TestContext.WriteLine(
                $"Phase7 load: {totalEvents} events / {gateway.Metrics.ReceivedBatches} batches, " +
                $"estimated payload {gateway.Metrics.EstimatedReceivedPayloadBytes} bytes, " +
                $"managed heap growth {managedGrowth} bytes, {timer.ElapsedMilliseconds} ms.");
        }

        [Test]
        public void GeneratedSandbox_UsesExistingGameplayPrefabsAndLocalOnlyProjectiles()
        {
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            GameObject enemy = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
            GameObject world = AssetDatabase.LoadAssetAtPath<GameObject>(WorldPrefabPath);
            GameObject projectile = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectilePrefabPath);

            Assert.That(player, Is.Not.Null);
            Assert.That(player.GetComponent<PlayerLoader>(), Is.Not.Null);
            Assert.That(player.GetComponent<PlayerHandBehaviour>(), Is.Not.Null);
            Assert.That(player.GetComponent<NetworkIdentity>(), Is.Not.Null);
            Assert.That(player.GetComponent<MirrorNetworkCombatBridge>(), Is.Not.Null);
            Assert.That(player.GetComponent<NetworkWeaponCombatAdapter>(), Is.Not.Null);
            Assert.That(player.GetComponent<NetworkPlayerBootstrap>(), Is.Not.Null);
            Assert.That(player.GetComponent<NetworkTransformReliable>().syncDirection,
                Is.EqualTo(SyncDirection.ClientToServer));

            Assert.That(enemy, Is.Not.Null);
            Assert.That(enemy.GetComponent<LocalEnemyChase>(), Is.Not.Null);
            Assert.That(enemy.GetComponent<LocalEnemyDeathBehaviour>(), Is.Null);
            Assert.That(enemy.GetComponent<NetworkEnemyServerDriver>(), Is.Not.Null);
            Assert.That(enemy.GetComponent<NetworkTransformReliable>().syncDirection,
                Is.EqualTo(SyncDirection.ServerToClient));

            Assert.That(world, Is.Not.Null);
            Assert.That(world.GetComponent<NetworkCombatWorld>(), Is.Not.Null);
            Assert.That(world.GetComponent<NetworkEnemySandboxSpawner>(), Is.Not.Null);
            Assert.That(projectile, Is.Not.Null);
            Assert.That(projectile.GetComponentsInChildren<NetworkBehaviour>(true), Is.Empty,
                "Projectiles remain owner-local and are never NetworkSpawned.");
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(SandboxScenePath), Is.Not.Null);
            AssertNoMissingScripts(player);
            AssertNoMissingScripts(enemy);
            AssertNoMissingScripts(world);
            AssertNoMissingScripts(projectile);
        }

        [Test]
        public void Sandbox_UsesMirrorLatencySimulationForTwoHundredMillisecondRttProfile()
        {
            Scene scene = EditorSceneManager.OpenScene(SandboxScenePath, OpenSceneMode.Additive);
            try
            {
                LatencySimulation simulation = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<LatencySimulation>(true))
                    .Single();
                NetworkManager manager = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<NetworkManager>(true))
                    .Single();

                Assert.That(simulation.latency, Is.EqualTo(100f),
                    "100 ms each direction models approximately 200 ms RTT.");
                Assert.That(simulation.jitter, Is.EqualTo(0.05f));
                Assert.That(simulation.unreliableLoss, Is.EqualTo(5f));
                Assert.That(manager.transport, Is.SameAs(simulation));
                Assert.That(manager.maxConnections, Is.EqualTo(4));
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    AssertNoMissingScripts(root);
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
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

        private static CombatSubmissionBatch StatusBatch(uint sequence, StatusMutation mutation)
        {
            return new CombatSubmissionBatch
            {
                BatchSequence = sequence,
                Results = Array.Empty<CombatResult>(),
                StatusMutations = new[] { mutation },
                PlayerHealthReports = Array.Empty<PlayerHealthReport>()
            };
        }

        private static StatusMutation Mutation(
            ulong eventId,
            ulong instanceId,
            uint sourcePlayerId,
            uint sourceEntityId,
            uint targetEntityId,
            int stackDelta)
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
                Duration = 3,
                ExecutionAuthority = (byte)StatusExecutionAuthority.SourceClient,
                TickDamage = 1,
                TotalTicks = 3,
                TickInterval = 1,
                Priority = 1,
                Magnitude = 0.25f,
                DamageSourceId = sourceEntityId
            };
        }

        private static CombatResult Result(
            CombatEventId eventId,
            uint playerId,
            uint sourceEntityId,
            uint targetEntityId,
            int damage)
        {
            return new CombatResult
            {
                EventId = eventId.Value,
                RootEventId = eventId.Value,
                Sequence = eventId.Sequence,
                SourcePlayerId = playerId,
                SourceEntityId = sourceEntityId,
                TargetEntityId = targetEntityId,
                AbilityId = 77,
                Damage = damage,
                DamageTags = (ulong)(CombatTags.Projectile | CombatTags.Damage),
                TargetStateVersion = 1
            };
        }

        private static StatusInstanceId CombatEventIdToStatusId(
            ushort sourceSlot,
            ushort epoch,
            uint sequence)
        {
            return new StatusInstanceId(CombatEventId.Compose(sourceSlot, epoch, sequence).Value);
        }

        private static void AssertNoMissingScripts(GameObject root)
        {
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                Assert.That(
                    GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                        transform.gameObject),
                    Is.Zero,
                    $"Missing script on {transform.gameObject.name} in {root.name}.");
            }
        }

        private sealed class SimulatedReliableLink
        {
            private readonly CombatSubmissionBatch batch;
            private bool firstAttemptHandled;
            private bool delivered;

            public SimulatedReliableLink(CombatSubmissionBatch batch)
            {
                this.batch = batch;
            }

            public bool TryReceive(double now, out CombatSubmissionBatch received)
            {
                // 100 ms one-way + up to 50 ms jitter. The first datagram is lost;
                // reliable retransmission makes it available at 400 ms.
                if (!firstAttemptHandled && now >= 0.15)
                {
                    firstAttemptHandled = true;
                }

                if (firstAttemptHandled && !delivered && now >= 0.40)
                {
                    delivered = true;
                    received = batch;
                    return true;
                }

                received = default;
                return false;
            }
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
                int applied = Math.Min(Health, requestedDamage.Value);
                Health -= applied;
                return new DamageInfo(requestedDamage.Id, applied, requestedDamage.IsCritical);
            }

            public StatusApplicationResult ApplyStatus(StatusApplication application) =>
                StatusApplicationResult.Added;
        }

        private sealed class TestModifierParameters : EquipmentModifierParameters
        {
        }

        private sealed class CountingPredictedLethalModifier : OnPredictedLethalHitModifier
        {
            public CountingPredictedLethalModifier(uint id)
                : base(new EquipmentModifierID(id), new TestModifierParameters())
            {
            }

            public int InvocationCount { get; private set; }
            public override float GetRollChance() => 1f;
            public override float GetRollPriority() => 1f;

            protected override void ApplyEffect(OnPredictedLethalHitModifierArgs args)
            {
                InvocationCount++;
            }
        }
    }
}
