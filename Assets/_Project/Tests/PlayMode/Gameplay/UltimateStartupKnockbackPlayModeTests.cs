using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Object = UnityEngine.Object;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class UltimateStartupKnockbackPlayModeTests
    {
        private const string BootPath = "Assets/_Project/Scenes/Boot.unity";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private BootGameplayNetworkManager manager;
        private GameObject[] bootRoots;
        private GameObject enemyPrefab;
        private readonly List<GameObject> objects = new List<GameObject>();
        private NetworkIdentity Player => NetworkClient.localPlayer;
        private NetworkPlayerUltimate Ultimate => Player.GetComponent<NetworkPlayerUltimate>();
        private NetworkEnemySimulationEndpoint Endpoint => Player.GetComponent<NetworkEnemySimulationEndpoint>();
        private NetworkEnemySimulationWorld World => NetworkEnemySimulationWorld.Instance;
        private DanteUltimateAttack Definition => (DanteUltimateAttack)Ultimate.Definition.ultimateAttackWeaponBehaviour;
        private uint AbilityId => UltimateNativeDefinitionAdapter.EncodeAbilityId(Ultimate.Definition.Id);

        [UnityTest]
        public IEnumerator Host_ClientAssignedProductEnemyMovesOnceThroughReliableEndpointWithoutAnyHealthMutation()
        {
            yield return StartHostFixture();
            NetworkEnemySimulationAgent enemy = SpawnEnemy(Vector2.right * 2f);
            yield return WaitFor(() => enemy.ProductEnemyInitialized && enemy.Authority.Role == EnemySimulationRole.ClientOwner,
                "The real product Enemy must initialize under this Host client's assignment.");
            Assert.That(enemy.ProductMovementOnly, Is.True);
            Assert.That(enemy.Assignment.Host, Is.EqualTo(EnemySimulationHost.ClientPlayer));
            Assert.That(enemy.Assignment.SimulationOwnerPlayerId, Is.EqualTo(Player.netId));
            var controller = enemy.GetComponent<EnemyController>();
            Assert.That(controller.StateMachine, Is.Null, "Movement-only must not initialize the old standalone combat FSM.");
            controller.Movement.StopMovement();
            Vector2 initial = enemy.GetComponent<Rigidbody2D>().position;
            HealthCapture health = CaptureHealth(enemy);
            ulong root = AdmitStartupWithoutOwnerWaves();
            Assert.That(World.RoutedUltimateKnockbackCount, Is.EqualTo(1));
            Assert.That(enemy.AppliedUltimateKnockbackCount, Is.Zero, "Host must not execute the client-assigned pulse on its server branch.");
            Assert.That(World.ServerApplyUltimateKnockback(Player.netId, root, AbilityId,
                Player.transform.position, Definition.KnockbackRadius, Definition.InitialKnockbackSettings), Is.Zero,
                "The same admitted root cannot route its startup pulse twice.");
            var duplicate = CommandFor(enemy, root, Field<ulong>(World, "knockbackCommandId"));
            yield return WaitFor(() => enemy.AppliedUltimateKnockbackCount == 1,
                "The reliable endpoint RPC must reach the assigned client simulator.");
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.That(enemy.GetComponent<Rigidbody2D>().position.x, Is.GreaterThan(initial.x),
                "The original knockback coroutine must move the product Rigidbody away from the pulse origin.");
            Assert.That(enemy.HasActiveNetworkKnockback, Is.True);
            AssertHealthUnchanged(enemy, health);
            int rejected = enemy.RejectedUltimateKnockbackCount;
            SendEndpoint(duplicate);
            yield return WaitFor(() => enemy.RejectedUltimateKnockbackCount == rejected + 1,
                "A repeated reliable command identity must be rejected by the receiving simulator.");
            Assert.That(enemy.AppliedUltimateKnockbackCount, Is.EqualTo(1));
            Assert.That(World.RoutedUltimateKnockbackCount, Is.EqualTo(1));
            yield return WaitFor(() => !enemy.HasActiveNetworkKnockback, "The original .6 second stagger must finish normally.");
            Assert.That(controller.Movement.CanMove, Is.False, "A startup impulse must preserve the fixture's independent navigation lock.");
            AssertHealthUnchanged(enemy, health);
            Assert.That(Ultimate.OwnerAttack, Is.Null, "The fixture isolated the authoritative startup pulse from the later damage waves.");
        }

        [UnityTest]
        public IEnumerator Host_ServerFallbackAndAuthoritativeRoutesMoveOnlyTheirServerBodiesAndSkipFrozenEnemy()
        {
            yield return StartHostFixture();
            NetworkEnemySimulationAgent fallback = SpawnEnemy(Vector2.right * 2f);
            NetworkEnemySimulationAgent authoritative = SpawnEnemy(Vector2.up * 2f);
            NetworkEnemySimulationAgent frozen = SpawnEnemy(Vector2.left * 2f);
            yield return WaitFor(() => fallback.ProductEnemyInitialized && authoritative.ProductEnemyInitialized && frozen.ProductEnemyInitialized,
                "Every real product Enemy must finish its network initialization.");
            fallback.SetServerAssignment(World.Registry.AssignServerFallback(fallback.netId, Player.netId));
            authoritative.SetServerAssignment(World.Registry.AssignServerAuthoritative(authoritative.netId, Player.netId));
            frozen.SetServerAssignment(World.Registry.Freeze(frozen.netId));
            // These registry APIs exercise simulator routing on the normal product prefab.
            // This is not a claim that the normal Enemy has been converted into a Boss.
            Assert.That(fallback.Authority.Role, Is.EqualTo(EnemySimulationRole.ServerFallback));
            Assert.That(authoritative.Authority.Role, Is.EqualTo(EnemySimulationRole.ServerAuthoritative));
            Assert.That(frozen.Authority.Role, Is.EqualTo(EnemySimulationRole.Frozen));
            fallback.GetComponent<EnemyController>().Movement.StopMovement();
            authoritative.GetComponent<EnemyController>().Movement.StopMovement();
            Vector2 first = fallback.GetComponent<Rigidbody2D>().position;
            Vector2 second = authoritative.GetComponent<Rigidbody2D>().position;
            HealthCapture firstHealth = CaptureHealth(fallback), secondHealth = CaptureHealth(authoritative), frozenHealth = CaptureHealth(frozen);
            ulong root = AdmitStartupWithoutOwnerWaves();
            Assert.That(World.RoutedUltimateKnockbackCount, Is.EqualTo(2));
            Assert.That(fallback.AppliedUltimateKnockbackCount, Is.EqualTo(1));
            Assert.That(authoritative.AppliedUltimateKnockbackCount, Is.EqualTo(1));
            Assert.That(frozen.AppliedUltimateKnockbackCount, Is.Zero);
            ulong lastCommand = Field<ulong>(World, "knockbackCommandId");
            SendEndpoint(CommandFor(fallback, root, lastCommand + 1u));
            SendEndpoint(CommandFor(authoritative, root, lastCommand + 2u));
            yield return WaitFor(() => fallback.RejectedUltimateKnockbackCount == 1 && authoritative.RejectedUltimateKnockbackCount == 1,
                "An endpoint presentation receiver cannot also execute a server-assigned impulse on Host.");
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.That(fallback.GetComponent<Rigidbody2D>().position.x, Is.GreaterThan(first.x));
            Assert.That(authoritative.GetComponent<Rigidbody2D>().position.y, Is.GreaterThan(second.y));
            Assert.That(fallback.AppliedUltimateKnockbackCount, Is.EqualTo(1));
            Assert.That(authoritative.AppliedUltimateKnockbackCount, Is.EqualTo(1));
            Assert.That(frozen.AppliedUltimateKnockbackCount, Is.Zero);
            AssertHealthUnchanged(fallback, firstHealth);
            AssertHealthUnchanged(authoritative, secondHealth);
            AssertHealthUnchanged(frozen, frozenHealth);
        }

        [UnityTest]
        public IEnumerator Host_AssignmentEpochChangeCancelsTheLiveCoroutineAndRejectsAnOldReliablePulse()
        {
            yield return StartHostFixture();
            NetworkEnemySimulationAgent enemy = SpawnEnemy(Vector2.right * 2f);
            yield return WaitFor(() => enemy.ProductEnemyInitialized && enemy.Authority.Role == EnemySimulationRole.ClientOwner,
                "The initial assigned client must be ready to receive its impulse.");
            enemy.GetComponent<EnemyController>().Movement.StopMovement();
            HealthCapture health = CaptureHealth(enemy);
            ulong root = AdmitStartupWithoutOwnerWaves();
            var oldEpoch = CommandFor(enemy, root, Field<ulong>(World, "knockbackCommandId") + 1u);
            yield return WaitFor(() => enemy.HasActiveNetworkKnockback, "The source movement coroutine must be active before reassignment.");
            yield return new WaitForFixedUpdate();
            var body = enemy.GetComponent<Rigidbody2D>();
            enemy.SetServerAssignment(World.Registry.Freeze(enemy.netId));
            // Hold this explicit no-simulator state; the production world normally reassigns
            // a Frozen Enemy to an eligible player in its following Update.
            World.enabled = false;
            Assert.That(enemy.Assignment.Epoch, Is.GreaterThan(oldEpoch.AssignmentEpoch));
            Assert.That(enemy.Authority.Role, Is.EqualTo(EnemySimulationRole.Frozen));
            Assert.That(enemy.HasActiveNetworkKnockback, Is.False);
            Assert.That(body.linearVelocity, Is.EqualTo(Vector2.zero));
            Vector2 stopped = body.position;
            SendEndpoint(oldEpoch);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;
            Assert.That(enemy.HasActiveNetworkKnockback, Is.False);
            Assert.That(enemy.AppliedUltimateKnockbackCount, Is.EqualTo(1));
            Assert.That(body.position, Is.EqualTo(stopped));
            Assert.That(body.linearVelocity, Is.EqualTo(Vector2.zero));
            Assert.That(World.PendingClientKnockbackCount, Is.Zero, "An obsolete epoch must be discarded, not retained for another assignment.");
            AssertHealthUnchanged(enemy, health);
        }

        private ulong AdmitStartupWithoutOwnerWaves()
        {
            Assert.That(Ultimate.ServerGrantCharge(), Is.True);
            Assert.That(Ultimate.RequestUse(), Is.True);
            ulong root = Field<ulong>(Ultimate, "pendingUseId");
            typeof(LocalConnectionToClient).GetMethod("Update", Private).Invoke(NetworkServer.localConnection, null);
            Assert.That(Ultimate.AcceptedUseCount, Is.EqualTo(1));
            Assert.That(NetworkCombatWorld.Instance.Gateway.Attacks.Contains(Player.netId, root, AbilityId), Is.True);
            Assert.That(Ultimate.OwnerAttack.IsNativeActive, Is.False);
            // The server has already routed startup pulses. Cancelling the pending presentation
            // stops the later damage waves from invalidating a movement-only health assertion.
            Ultimate.enabled = false;
            return root;
        }
        private EnemyKnockbackCommand CommandFor(NetworkEnemySimulationAgent enemy, ulong root, ulong commandId) => new EnemyKnockbackCommand
        {
            EnemyEntityId = enemy.netId, AssignmentEpoch = enemy.Assignment.Epoch,
            SourcePlayerId = Player.netId, AbilityCombatId = AbilityId, RootEventId = root, CommandId = commandId,
            IssuedAt = NetworkTime.time, Origin = Player.transform.position,
            Settings = EnemyKnockbackSettings.From(Definition.InitialKnockbackSettings)
        };
        private void SendEndpoint(EnemyKnockbackCommand command) => typeof(NetworkEnemySimulationEndpoint)
            .GetMethod("TargetApplyUltimateKnockback", Private).Invoke(Endpoint, new object[] { Player.connectionToClient, command });
        private NetworkEnemySimulationAgent SpawnEnemy(Vector2 offset)
        {
            GameObject instance = Object.Instantiate(enemyPrefab, Player.transform.position + (Vector3)offset, Quaternion.identity);
            objects.Add(instance);
            SceneManager.MoveGameObjectToScene(instance, Player.gameObject.scene);
            var agent = instance.GetComponent<NetworkEnemySimulationAgent>();
            Assert.That(agent, Is.Not.Null);
            agent.ConfigureRuntimeMinimumHealthOverride(10000);
            agent.ConfigureInitialServerTarget(Player.netId);
            NetworkServer.Spawn(instance);
            return agent;
        }
        private static HealthCapture CaptureHealth(NetworkEnemySimulationAgent enemy)
        {
            Assert.That(NetworkCombatWorld.Instance.Gateway.Ledger.TryGetState(enemy.netId, out var state), Is.True);
            return new HealthCapture { Canonical = state.Health, Combatant = enemy.GetComponent<CombatantBehaviour>().CurrentHealth,
                Product = enemy.GetComponent<EnemyController>().CurrentHealth };
        }
        private static void AssertHealthUnchanged(NetworkEnemySimulationAgent enemy, HealthCapture before)
        {
            HealthCapture after = CaptureHealth(enemy);
            Assert.That(after.Canonical, Is.EqualTo(before.Canonical));
            Assert.That(after.Combatant, Is.EqualTo(before.Combatant));
            Assert.That(after.Product, Is.EqualTo(before.Product));
        }
        private struct HealthCapture { public int Canonical, Combatant, Product; }
        private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Private).GetValue(value);
        private GameObject Create(string name) { var value = new GameObject(name); objects.Add(value); return value; }
        private IEnumerator StartHostFixture()
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(BootPath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(BootPath, LoadSceneMode.Single);
#endif
            bootRoots = BootSceneFixtureObjects.Capture(BootPath);
            manager = Object.FindFirstObjectByType<BootGameplayNetworkManager>();
            Assert.That(manager, Is.Not.Null);
            Assert.That(manager.GetComponent<NetworkBackendBootstrap>().TryPrepareKcp("127.0.0.1", 7954, false, out string error), Is.True, error);
            Create("Ultimate startup fixture gate").AddComponent<WeaponAttackAdmissionFixtureGate>();
            SceneManager.sceneLoaded += PrepareGameplay;
            manager.StartHost();
            yield return WaitFor(() => manager.IsGameplayLoaded && Player != null &&
                Player.GetComponent<NetworkModifierSelection>().HasOwnerBaseline && Ultimate != null && Ultimate.OwnerAttack != null,
                "Boot must create the owned Ultimate and enemy simulation endpoint before this fixture runs.");
            Assert.That(enemyPrefab, Is.Not.Null, "The fixture must use Gameplay's configured product Enemy prefab.");
            var movement = Player.GetComponent<PlayerMovement>();
            movement.body.position = new Vector2(1000, 1000);
            movement.transform.position = new Vector3(1000, 1000, 0);
            movement.SetDirection(Vector2.zero);
            Physics2D.SyncTransforms();
        }
        private void PrepareGameplay(Scene scene, LoadSceneMode mode)
        {
            if (scene.path != "Assets/_Project/Scenes/Gameplay.unity") return;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (var spawner in root.GetComponentsInChildren<NetworkGameplayEnemySpawner>(true))
                { enemyPrefab = spawner.EnemyPrefab; spawner.enabled = false; }
        }
        private static IEnumerator WaitFor(Func<bool> condition, string message)
        {
            float deadline = Time.realtimeSinceStartup + 20f;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, message);
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SceneManager.sceneLoaded -= PrepareGameplay;
            if (manager != null)
            {
                if (NetworkServer.active && NetworkClient.active) manager.StopHost();
                else if (NetworkServer.active) manager.StopServer();
                else if (NetworkClient.active) manager.StopClient();
                yield return WaitFor(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning, "Gameplay must unload.");
            }
            for (int index = objects.Count - 1; index >= 0; index--) if (objects[index] != null) Object.Destroy(objects[index]);
            objects.Clear(); enemyPrefab = null;
            BootSceneFixtureObjects.Destroy(bootRoots);
            yield return null;
            NetworkManager.ResetStatics();
        }
    }
}
