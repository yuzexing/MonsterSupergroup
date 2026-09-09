using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using kcp2k;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class NetworkEnemyDebugPanelTests
    {
        private GameObject network;
        private NetworkManager manager;
        private NetworkCombatWorld world;
        private NetworkEnemyDebugPanel panel;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            network = new GameObject("Enemy debug Host test");
            network.SetActive(false);
            var transport = network.AddComponent<KcpTransport>();
            transport.Port = 0;
            manager = network.AddComponent<NetworkManager>();
            manager.transport = transport;
            manager.autoCreatePlayer = false;
            manager.dontDestroyOnLoad = false;
            network.SetActive(true);
            var worldObject = new GameObject("Enemy debug world");
            worldObject.SetActive(false);
            world = worldObject.AddComponent<NetworkCombatWorld>();
            world.gameObject.AddComponent<NetworkEnemySimulationWorld>();
            worldObject.SetActive(true);
            panel = new GameObject("Enemy debug panel").AddComponent<NetworkEnemyDebugPanel>();
            manager.StartHost();
            yield return WaitFor(() => NetworkClient.ready);
            NetworkServer.Spawn(world.gameObject);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (panel != null) Object.DestroyImmediate(panel.gameObject);
            if (manager != null && NetworkServer.active) manager.StopHost();
            yield return null;
            if (world != null) Object.DestroyImmediate(world.gameObject);
            if (network != null) Object.DestroyImmediate(network);
        }

        [UnityTest]
        public IEnumerator ReadsBaseline_DistinguishesPrediction_AndNeverChangesHealthOrStatus()
        {
            var enemy = SpawnEnemy();
            var combatant = enemy.GetComponent<CombatantBehaviour>();
            var status = new CanonicalStatusState
            {
                InstanceId = 501, DefinitionId = (uint)EnemyStatusID.Burn,
                StackMode = (byte)StatusStackMode.Add, MaxStacks = 10, Stack = 2,
                TargetEntityId = enemy.netId, SourcePlayerId = 7, SourceEntityId = 7,
                Duration = 30, Version = 1, TickInterval = 1, TotalTicks = 3
            };
            world.Replica.RegisterStatusController(enemy.netId, combatant.StatusController);
            panel.enabled = false;
            Apply(State(enemy, 80), status); // Baseline predates the UI subscription.
            combatant.ReceiveDamage(new DamageInfo(1, 45, false));
            combatant.StatusController.ApplyPredictedStackDelta(new StatusInstanceId(501), -1);
            panel.enabled = true;
            yield return WaitFor(() => panel.Rows.Count == 1);
            var row = panel.Rows[0];
            Assert.That(row.Canonical.Value.Health, Is.EqualTo(80));
            Assert.That(row.LocalHealth, Is.EqualTo("55 / 100"));
            Assert.That(row.CanonicalStatuses, Is.EqualTo("Burn x2"));
            Assert.That(row.Text, Does.Contain("Local HP (may include prediction)"));
            Assert.That(combatant.CurrentHealth, Is.EqualTo(55));
            Assert.That(combatant.StatusController.GetStackCount(EnemyStatusID.Burn), Is.EqualTo(1));

            panel.SetExpanded(false);
            Apply(State(enemy, 60, 2), CanonicalStatusState.Removal(new StatusInstanceId(501), 2));
            yield return WaitFor(() => panel.Rows[0].Canonical.Value.Health == 60);
            Assert.That(panel.Rows[0].CanonicalStatuses, Is.EqualTo("none"));
            panel.SetExpanded(true);
            Assert.That(combatant.CurrentHealth, Is.EqualTo(55));
            Assert.That(world.Replica.TryGetEntity(enemy.netId, out var canonical), Is.True);
            Assert.That(canonical.Health, Is.EqualTo(60));
        }

        [UnityTest]
        public IEnumerator MissingCanonicalAndAssignmentAreUnavailable_NotGuessedFromLocalHealth()
        {
            var enemy = SpawnEnemy();
            // A replica can appear before its assignment arrives.
            typeof(NetworkEnemySimulationAgent).GetField("assignment", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(enemy, default(EnemySimulationAssignment));
            enemy.GetComponent<CombatantBehaviour>().ReceiveDamage(new DamageInfo(2, 100, false));
            yield return WaitFor(() => panel.Rows.Count == 1);
            Assert.That(panel.Rows[0].Canonical, Is.Null);
            Assert.That(panel.Rows[0].Text, Does.Contain("waiting for canonical snapshot"));
            Assert.That(panel.Rows[0].CanonicalStatuses, Does.Contain("unavailable"));
            Assert.That(panel.Rows[0].Simulation, Does.Contain("assignment pending"));
            Assert.That(panel.ConnectionText, Does.Contain("Local player: unavailable"));
        }

        [UnityTest]
        public IEnumerator OnlySpawnedAgentsAppear_Sorted_AndOrdinaryDespawnLeavesNoDeathRow()
        {
            var first = SpawnEnemy();
            var second = SpawnEnemy();
            var other = new GameObject("Non enemy network object").AddComponent<NetworkIdentity>();
            NetworkServer.Spawn(other.gameObject);
            world.Replica.Apply(new CanonicalWorldBatch { Entities = new[]
                { State(second, 90), State(first, 70), new CanonicalEntityState
                    { EntityId = 999999, Kind = (byte)CombatEntityKind.Enemy, Health = 1, Alive = true } } });
            yield return WaitFor(() => panel.Rows.Count == 2);
            Assert.That(panel.Rows.Select(row => row.EntityId), Is.EqualTo(new[] { first.netId, second.netId }));
            NetworkServer.Destroy(first.gameObject);
            yield return WaitFor(() => panel.Rows.Count == 1);
            Assert.That(panel.Rows[0].EntityId, Is.EqualTo(second.netId));
        }

        [UnityTest]
        public IEnumerator CanonicalDeathSurvivesDestructionForTwoSeconds_DuplicateDoesNotExtendIt()
        {
            var enemy = SpawnEnemy();
            uint id = enemy.netId;
            Apply(State(enemy, 80));
            yield return WaitFor(() => panel.Rows.Count == 1);
            var dead = State(enemy, 0, 2);
            Apply(dead);
            NetworkServer.Destroy(enemy.gameObject);
            world.Replica.ForgetEntity(id);
            yield return WaitFor(() => panel.Rows.Count == 1 && panel.Rows[0].Canonical.Value.Health == 0);
            Assert.That(panel.Rows[0].Text, Does.Contain("Dead"));
            yield return new WaitForSecondsRealtime(1.1f);
            Apply(dead);
            yield return new WaitForSecondsRealtime(1.2f);
            Assert.That(panel.Rows, Is.Empty);
        }

        [UnityTest]
        public IEnumerator DestructionBeforeQueuedCanonicalDeathUsesLastObservedData()
        {
            var enemy = SpawnEnemy();
            uint id = enemy.netId;
            Apply(State(enemy, 40));
            yield return WaitFor(() => panel.Rows.Count == 1);
            var dead = State(enemy, 0, 2);
            NetworkServer.Destroy(enemy.gameObject);
            world.Replica.ForgetEntity(id);
            yield return WaitFor(() => panel.Rows.Count == 0);
            Apply(dead); // Host receives its queued RPC after the server destroys the shared object.
            yield return WaitFor(() => panel.Rows.Count == 1);
            Assert.That(panel.Rows[0].Canonical.Value.Alive, Is.False);
            Assert.That(panel.Rows[0].Text, Does.Contain("Recent death - last observed data"));
            Assert.That(panel.Rows[0].Text, Does.Contain("Debug observed enemy"));
            Assert.That(NetworkClient.spawned.ContainsKey(id), Is.False);
            yield return new WaitForSecondsRealtime(2.3f);
            Assert.That(panel.Rows, Is.Empty);
        }

        [UnityTest]
        public IEnumerator DisableReenableWorldReplacementAndDisconnectReleaseSubscriptionsAndStaleRows()
        {
            var enemy = SpawnEnemy();
            Apply(State(enemy, 75));
            yield return WaitFor(() => panel.Rows.Count == 1);
            Assert.That(SubscriberCount(world), Is.EqualTo(1));
            for (int i = 0; i < 3; i++)
            {
                panel.enabled = false;
                Assert.That(SubscriberCount(world), Is.Zero);
                Assert.That(panel.Rows, Is.Empty);
                panel.enabled = true;
                yield return WaitFor(() => panel.Rows.Count == 1);
                Assert.That(SubscriberCount(world), Is.EqualTo(1));
            }
            world.enabled = false;
            yield return WaitFor(() => panel.Rows.Count == 0);
            Assert.That(SubscriberCount(world), Is.Zero);
            world.enabled = true;
            yield return WaitFor(() => panel.Rows.Count == 1);
            NetworkServer.Destroy(world.gameObject);
            yield return null;
            world = new GameObject("Replacement debug world").AddComponent<NetworkCombatWorld>();
            NetworkServer.Spawn(world.gameObject);
            yield return WaitFor(() => panel.Rows.Count == 1 && !panel.Rows[0].Canonical.HasValue);
            Assert.That(SubscriberCount(world), Is.EqualTo(1));
            manager.StopHost();
            yield return WaitFor(() => panel.Rows.Count == 0);
            Assert.That(panel.ConnectionText, Does.Contain("Connected: False"));
        }

        private NetworkEnemySimulationAgent SpawnEnemy()
        {
            var root = new GameObject("Debug observed enemy");
            root.SetActive(false);
            root.AddComponent<CombatantBehaviour>().Initialize(100);
            var agent = root.AddComponent<NetworkEnemySimulationAgent>();
            root.SetActive(true);
            NetworkServer.Spawn(root);
            return agent;
        }

        private static CanonicalEntityState State(NetworkEnemySimulationAgent enemy, int health, uint version = 1)
            => new CanonicalEntityState { EntityId = enemy.netId, Kind = (byte)CombatEntityKind.Enemy,
                Health = health, MaxHealth = 100, Alive = health > 0, StateVersion = version };

        private void Apply(CanonicalEntityState state, params CanonicalStatusState[] statuses)
            => world.Replica.Apply(new CanonicalWorldBatch { Entities = new[] { state }, Statuses = statuses });

        private static int SubscriberCount(NetworkCombatWorld value)
            => ((Delegate)typeof(CanonicalWorldReplica).GetField("EntityChanged", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(value.Replica))?.GetInvocationList().Length ?? 0;

        private static IEnumerator WaitFor(Func<bool> predicate)
        {
            float deadline = Time.realtimeSinceStartup + 5f;
            while (!predicate() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(predicate(), Is.True, "Enemy Debug condition did not become true within five seconds.");
        }
    }
}
