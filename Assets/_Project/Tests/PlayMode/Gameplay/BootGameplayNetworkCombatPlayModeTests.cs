using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.Controllers;
using AstralShift.HellMaiden.Player;
using AstralShift.Managers;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.Local;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class BootGameplayNetworkCombatPlayModeTests
    {
        private const string BootScenePath = "Assets/Scenes/Boot.unity";
        private const string GameplayScenePath = "Assets/Scenes/Gameplay.unity";

        [UnityTest]
        public IEnumerator Host_CompletesPlayerAndSkeletonCanonicalCombatLoops()
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
                BootScenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(BootScenePath, LoadSceneMode.Single);
#endif
            BootGameplayNetworkManager manager =
                Object.FindFirstObjectByType<BootGameplayNetworkManager>();
            Assert.That(manager, Is.Not.Null);
            Assert.That(NetworkCombatWorld.Instance, Is.Not.Null,
                "The canonical World must exist in Boot before Player spawn.");

            SteamLobbyService lobbyService =
                manager.GetComponent<SteamLobbyService>();
            KcpLocalNetworkService kcpService =
                manager.GetComponent<KcpLocalNetworkService>();
            NetworkBackendBootstrap backendBootstrap =
                manager.GetComponent<NetworkBackendBootstrap>();
            Assert.That(backendBootstrap, Is.Not.Null);
            Assert.That(backendBootstrap.IsInitialized, Is.True);
            Assert.That(
                backendBootstrap.Selection.Backend,
                Is.EqualTo(NetworkBackendKind.Kcp));
            Assert.That(
                backendBootstrap.Selection.Purpose,
                Is.EqualTo(NetworkRuntimePurpose.Test));
            Assert.That(lobbyService, Is.Not.Null);
            Assert.That(kcpService, Is.Not.Null);
            Assert.That(kcpService.IsKcpBackendSelected, Is.True);
            Assert.That(kcpService.IsInteractiveKcp, Is.False);
            Assert.That(lobbyService.IsValidationBypass, Is.True);
            Assert.That(lobbyService.IsSteamInitialized, Is.False);
            Assert.That(lobbyService.State, Is.EqualTo(SteamLobbyState.Disabled));
            Assert.That(
                backendBootstrap.ConfiguredSteamTransport.enabled,
                Is.False,
                "Fizzy stays disabled in test runs.");
            Assert.That(manager.transport.enabled, Is.False,
                "KCP stays idle until the test explicitly starts Mirror.");

            BootGameplayProcessValidationBootstrap validationBootstrap =
                manager.GetComponent<BootGameplayProcessValidationBootstrap>();
            Assert.That(validationBootstrap, Is.Not.Null);
            Transport validationTransport =
                validationBootstrap.ConfiguredValidationTransport;
            Assert.That(validationTransport, Is.Not.Null,
                "Boot must retain the KCP/Latency validation fallback.");
            Assert.That(validationTransport, Is.InstanceOf<PortTransport>());
            bool prepared = backendBootstrap.TryPrepareKcp(
                NetworkBackendBootstrap.DefaultKcpAddress,
                NetworkBackendBootstrap.DefaultKcpPort,
                true,
                out string prepareError);
            Assert.That(prepared, Is.True, prepareError);
            Assert.That(manager.transport, Is.SameAs(validationTransport));
            Assert.That(Transport.active, Is.SameAs(validationTransport));
            manager.StartHost();
            // The recovered Dante projectile intentionally retains its original
            // FMOD event GUID, while that source bank is not present in this
            // repository. This is a known presentation-content gap and is not
            // part of the combat/network authority loop under test.
            LogAssert.Expect(
                LogType.Exception,
                new Regex(@"EventNotFoundException: \[FMOD\] Event not found:.*"));

            float startupDeadline = Time.realtimeSinceStartup + 12f;
            NetworkEnemySimulationAgent skeleton = null;
            PlayerBuildRuntime build = null;
            while (Time.realtimeSinceStartup < startupDeadline)
            {
                skeleton = Object.FindObjectsByType<NetworkEnemySimulationAgent>(
                        FindObjectsSortMode.None)
                    .FirstOrDefault(agent =>
                        agent != null && agent.name.Contains("NetworkEnemySkeleton"));
                build = NetworkClient.localPlayer != null
                    ? NetworkClient.localPlayer.GetComponent<PlayerBuildRuntime>()
                    : null;
                if (manager.IsGameplayLoaded && NetworkClient.localPlayer != null &&
                    skeleton != null && skeleton.ProductEnemyInitialized &&
                    skeleton.Authority.Role == EnemySimulationRole.ClientOwner &&
                    build != null && build.IsBuildActive)
                {
                    break;
                }

                yield return null;
            }

            Assert.That(manager.IsGameplayLoaded, Is.True);
            Assert.That(SceneManager.GetActiveScene().path,
                Is.EqualTo(GameplayScenePath));
            Assert.That(NetworkClient.localPlayer, Is.Not.Null);
            Assert.That(NetworkClient.localPlayer.gameObject.scene.path,
                Is.EqualTo(GameplayScenePath));
            Assert.That(skeleton, Is.Not.Null);
            Assert.That(skeleton.gameObject.scene.path, Is.EqualTo(GameplayScenePath));
            Assert.That(build, Is.Not.Null);
            Assert.That(build.IsBuildActive, Is.True);
            Assert.That(build.InitialWeapon, Is.Not.Null);
            Assert.That(build.InitialWeapon.UsesNativeGasRuntime, Is.True);

            PlayerMovement ownerMovement =
                NetworkClient.localPlayer.GetComponent<PlayerMovement>();
            Assert.That(ownerMovement, Is.Not.Null);
            Assert.That(ownerMovement.enabled, Is.True,
                "Only the Owner may execute PlayerMovement.");
            Assert.That(ControllerManager.Instance, Is.Not.Null);
            Assert.That(
                ControllerManager.Instance.CurrentController,
                Is.TypeOf<PlayerController_HMD>(),
                "OnStartAuthority must activate the gameplay input controller.");
            Assert.That(
                ControllerManager.Instance.inputHandler.CurrentController,
                Is.SameAs(ControllerManager.Instance.CurrentController));

            Rigidbody2D ownerBody = ownerMovement.GetComponent<Rigidbody2D>();
            Vector2 movementStart = ownerBody.position;
            ownerMovement.SetDirection(Vector2.right);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            ownerMovement.SetDirection(Vector2.zero);
            Assert.That(ownerBody.position.x, Is.GreaterThan(movementStart.x + 0.01f),
                "PlayerMovement must remain the active movement executor.");

            uint playerId = NetworkClient.localPlayer.netId;
            Assert.That(skeleton.Assignment.AggroTargetPlayerId, Is.EqualTo(playerId));
            Assert.That(
                skeleton.Assignment.SimulationOwnerPlayerId,
                Is.EqualTo(playerId));
            Assert.That(
                skeleton.Assignment.Host,
                Is.EqualTo(EnemySimulationHost.ClientPlayer));
            Assert.That(
                skeleton.Authority.Role,
                Is.EqualTo(EnemySimulationRole.ClientOwner));

            NetworkPlayerAutoTargeting autoTargeting =
                NetworkClient.localPlayer.GetComponent<NetworkPlayerAutoTargeting>();
            CombatantBehaviour enemyCombatant =
                skeleton.GetComponent<CombatantBehaviour>();
            float targetingDeadline = Time.realtimeSinceStartup + 3f;
            while ((autoTargeting == null ||
                    autoTargeting.CurrentTarget != enemyCombatant) &&
                   Time.realtimeSinceStartup < targetingDeadline)
            {
                yield return null;
            }
            Assert.That(autoTargeting, Is.Not.Null);
            Assert.That(autoTargeting.enabled, Is.True);
            Assert.That(autoTargeting.CurrentTarget, Is.SameAs(enemyCombatant));

            NetworkCombatWorld world = NetworkCombatWorld.Instance;
            Assert.That(world, Is.Not.Null);
            Assert.That(
                world.Gateway.Ledger.TryGetState(
                    skeleton.netId,
                    out CanonicalEntityState initialEnemyState),
                Is.True);
            int initialEnemyHealth = initialEnemyState.Health;
            CanonicalEntityState damagedEnemyState = initialEnemyState;
            float enemyDamageDeadline = Time.realtimeSinceStartup + 10f;
            while ((!world.Gateway.Ledger.TryGetState(
                        skeleton.netId,
                        out damagedEnemyState) ||
                    damagedEnemyState.Health >= initialEnemyHealth) &&
                   Time.realtimeSinceStartup < enemyDamageDeadline)
            {
                yield return null;
            }

            Assert.That(damagedEnemyState.Health, Is.LessThan(initialEnemyHealth),
                "A real Dante projectile must reach EnemyHurtbox and Server Ledger.");
            Assert.That(enemyCombatant.CurrentHealth,
                Is.EqualTo(damagedEnemyState.Health));
            Assert.That(damagedEnemyState.Alive, Is.True);
            build.ClearBuild();

            PlayerCombatantBinding playerBinding =
                NetworkClient.localPlayer.GetComponent<PlayerCombatantBinding>();
            PlayerHitbox playerHitbox = NetworkClient.localPlayer
                .GetComponentInChildren<PlayerHitbox>(true);
            Assert.That(playerBinding, Is.Not.Null);
            Assert.That(playerHitbox, Is.Not.Null);
            int initialPlayerHealth = playerBinding.CurrentHealth;

            Rigidbody2D skeletonBody = skeleton.GetComponent<Rigidbody2D>();
            Vector2 attackPosition = playerHitbox.transform.position;
            skeletonBody.position = attackPosition;
            skeleton.transform.position = attackPosition;
            Physics2D.SyncTransforms();

            int minimumPlayerHealth = initialPlayerHealth;
            void CapturePlayerHealth(int current, int maximum)
            {
                minimumPlayerHealth = Mathf.Min(minimumPlayerHealth, current);
            }
            playerBinding.Combatant.HealthChanged += CapturePlayerHealth;
            float playerDamageDeadline = Time.realtimeSinceStartup + 10f;
            while (minimumPlayerHealth >= initialPlayerHealth &&
                   Time.realtimeSinceStartup < playerDamageDeadline)
            {
                yield return null;
            }
            playerBinding.Combatant.HealthChanged -= CapturePlayerHealth;

            Assert.That(minimumPlayerHealth, Is.LessThan(initialPlayerHealth),
                "Skeleton melee must resolve against the local PlayerHitbox.");
            CanonicalEntityState canonicalPlayer = default;
            float playerReportDeadline = Time.realtimeSinceStartup + 3f;
            while ((!world.Gateway.Ledger.TryGetState(playerId, out canonicalPlayer) ||
                    canonicalPlayer.Health != minimumPlayerHealth) &&
                   Time.realtimeSinceStartup < playerReportDeadline)
            {
                yield return null;
            }

            Assert.That(canonicalPlayer.Health, Is.EqualTo(minimumPlayerHealth));
            Assert.That(playerBinding.CurrentHealth, Is.EqualTo(minimumPlayerHealth));
            Assert.That(world.Gateway.Metrics.AcceptedPlayerReports,
                Is.GreaterThan(0));

            kcpService.Stop();
            float cleanupDeadline = Time.realtimeSinceStartup + 5f;
            while ((NetworkServer.active || NetworkClient.active ||
                    manager.IsGameplayLoaded ||
                    manager.mode != NetworkManagerMode.Offline ||
                    kcpService.State == KcpLocalNetworkState.Stopping) &&
                   Time.realtimeSinceStartup < cleanupDeadline)
            {
                yield return null;
            }

            Assert.That(NetworkServer.active, Is.False);
            Assert.That(NetworkClient.active, Is.False);
            Assert.That(manager.mode, Is.EqualTo(NetworkManagerMode.Offline));
            Assert.That(manager.IsGameplayLoaded, Is.False);
            Assert.That(kcpService.State,
                Is.EqualTo(KcpLocalNetworkState.Idle));
            Assert.That(
                backendBootstrap.ConfiguredKcpTransport.enabled,
                Is.False);
            Assert.That(
                backendBootstrap.ConfiguredLatencySimulation.enabled,
                Is.False);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            NetworkManager manager = NetworkManager.singleton;
            if (manager != null)
            {
                if (NetworkServer.active && NetworkClient.active)
                {
                    manager.StopHost();
                }
                else if (NetworkServer.active)
                {
                    manager.StopServer();
                }
                else if (NetworkClient.active)
                {
                    manager.StopClient();
                }
            }

            float deadline = Time.realtimeSinceStartup + 5f;
            while ((NetworkServer.active || NetworkClient.active ||
                    SceneManager.GetSceneByPath(GameplayScenePath).isLoaded) &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            if (ControllerManager.Instance != null)
            {
                Assert.That(ControllerManager.Instance.CurrentController, Is.Null,
                    "Stopping Owner authority must release PlayerController_HMD.");
            }
        }
    }
}
