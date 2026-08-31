using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using AstralShift.HellMaiden.AI;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.GAS;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MonsterSupergroup.NetworkCombat
{
    public enum BootGameplayValidationRole : byte
    {
        None = 0,
        Host = 1,
        Client = 2
    }

    public readonly struct BootGameplayValidationOptions
    {
        public BootGameplayValidationOptions(
            BootGameplayValidationRole role,
            string address,
            ushort port,
            float timeoutSeconds)
        {
            Role = role;
            Address = address;
            Port = port;
            TimeoutSeconds = timeoutSeconds;
        }

        public BootGameplayValidationRole Role { get; }
        public string Address { get; }
        public ushort Port { get; }
        public float TimeoutSeconds { get; }
    }

    /// <summary>
    /// Opt-in two-process validation for the product Boot -> Gameplay loop. It is
    /// inert unless --boot-gameplay-role=host|client is supplied.
    /// </summary>
    [DefaultExecutionOrder(-20000)]
    [DisallowMultipleComponent]
    public sealed class BootGameplayProcessValidationBootstrap : MonoBehaviour
    {
        private const string LogPrefix = "[BootGameplayProcessValidation]";
        private const string GameplayScenePath = "Assets/Scenes/Gameplay.unity";
        private const float DefaultTimeoutSeconds = 50f;
        private const int ValidationEnemyMinimumHealth = 1000;

        [SerializeField] private BootGameplayNetworkManager networkManager;
        [SerializeField, Min(0f)] private float minimumConnectedSeconds = 5f;
        [SerializeField, Min(0f)] private float quitGraceSeconds = 2f;

        private BootGameplayValidationOptions options;
        private float validationStartedAt;
        private float twoPlayerTopologyObservedAt = -1f;
        private int localInitialHealth = -1;
        private uint localPlayerId;
        private uint remotePlayerId;
        private uint remoteEnemyId;
        private uint remoteEnemyAssignmentEpoch;
        private bool hostReadyForLateJoin;
        private bool remoteEverConnected;
        private bool twoPlayerTopologyObserved;
        private bool localBuildObserved;
        private bool localAutoTargetObserved;
        private bool localAssignedEnemyTargetObserved;
        private bool localDamageTraceObserved;
        private bool localBuildStoppedAfterDamage;
        private bool replicatedEnemyDamageObserved;
        private bool localPlayerHealthDecreased;
        private bool assignmentTopologyObserved;
        private bool localOwnerRoleObserved;
        private bool replicaRoleObserved;
        private bool serverCanonicalEnemyDamageObserved;
        private bool serverCanonicalPlayersDamaged;
        private bool preDisconnectObserved;
        private bool disconnectRequested;
        private bool finished;
        private int currentPlayerCount;
        private int currentEnemyCount;
        private bool currentEnemiesAlive;

        public BootGameplayNetworkManager ConfiguredNetworkManager =>
            networkManager;

        public void Configure(BootGameplayNetworkManager manager)
        {
            networkManager = manager != null
                ? manager
                : throw new ArgumentNullException(nameof(manager));
            minimumConnectedSeconds = 5f;
            quitGraceSeconds = 2f;
        }

        private IEnumerator Start()
        {
            if (!TryParseOptions(
                    Environment.GetCommandLineArgs(),
                    out options,
                    out string error))
            {
                if (!string.IsNullOrEmpty(error))
                {
                    Finish(false, error);
                }
                yield break;
            }

            if (networkManager == null)
            {
                Finish(false, "Boot validation NetworkManager is missing.");
                yield break;
            }
            if (!(networkManager.transport is PortTransport portTransport))
            {
                Finish(false, "Configured Mirror transport does not expose a port.");
                yield break;
            }

            Application.runInBackground = true;
            networkManager.networkAddress = options.Address;
            portTransport.Port = options.Port;
            validationStartedAt = Time.realtimeSinceStartup;

            if (options.Role == BootGameplayValidationRole.Host)
            {
                SceneManager.sceneLoaded += HandleValidationSceneLoaded;
                networkManager.StartHost();
            }
            else
            {
                networkManager.StartClient();
            }

            Debug.Log(
                $"{LogPrefix} event=network-started role={options.Role} " +
                $"address={options.Address} port={options.Port}");

            while (!finished &&
                   Time.realtimeSinceStartup - validationStartedAt <
                       options.TimeoutSeconds)
            {
                ObserveCurrentState();
                if (options.Role == BootGameplayValidationRole.Host)
                {
                    ObserveHostLifecycle();
                }
                else
                {
                    ObserveClientLifecycle();
                }
                yield return null;
            }

            if (!finished)
            {
                Finish(false, BuildSummary());
            }
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= HandleValidationSceneLoaded;
        }

        private void HandleValidationSceneLoaded(
            Scene scene,
            LoadSceneMode mode)
        {
            if (options.Role != BootGameplayValidationRole.Host ||
                scene.path != GameplayScenePath)
            {
                return;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                NetworkGameplayEnemySpawner spawner =
                    roots[i].GetComponentInChildren<
                        NetworkGameplayEnemySpawner>(true);
                if (spawner == null)
                {
                    continue;
                }

                spawner.ConfigureRuntimeMinimumSpawnHealth(
                    ValidationEnemyMinimumHealth);
                Debug.Log(
                    $"{LogPrefix} event=validation-enemy-health-configured " +
                    $"minimum={ValidationEnemyMinimumHealth}");
                return;
            }

            Debug.LogError(
                $"{LogPrefix} Gameplay scene has no Enemy spawner.");
        }

        private void ObserveCurrentState()
        {
            NetworkIdentity localPlayer = NetworkClient.localPlayer;
            if (localPlayer != null)
            {
                localPlayerId = localPlayer.netId;
                CombatantBehaviour combatant =
                    localPlayer.GetComponent<CombatantBehaviour>();
                if (combatant != null)
                {
                    if (localInitialHealth < 0)
                    {
                        localInitialHealth = combatant.MaxHealth;
                    }
                    localPlayerHealthDecreased |=
                        combatant.CurrentHealth < localInitialHealth;
                }

                PlayerBuildRuntime build =
                    localPlayer.GetComponent<PlayerBuildRuntime>();
                localBuildObserved |= build != null && build.IsBuildActive &&
                    build.InitialWeapon != null &&
                    build.InitialWeapon.UsesNativeGasRuntime;

                NetworkPlayerAutoTargeting autoTargeting =
                    localPlayer.GetComponent<NetworkPlayerAutoTargeting>();
                localAutoTargetObserved |= autoTargeting != null &&
                    autoTargeting.enabled && autoTargeting.CurrentTarget != null;
                if (autoTargeting != null &&
                    autoTargeting.CurrentTarget != null)
                {
                    NetworkEnemySimulationAgent targetedEnemy =
                        autoTargeting.CurrentTarget.GetComponent<
                            NetworkEnemySimulationAgent>();
                    localAssignedEnemyTargetObserved |= targetedEnemy != null &&
                        targetedEnemy.Assignment.SimulationOwnerPlayerId ==
                            localPlayerId;
                }

                MirrorNetworkCombatBridge bridge =
                    localPlayer.GetComponent<MirrorNetworkCombatBridge>();
                if (bridge != null && bridge.Trace != null &&
                    bridge.Trace.Count > 0)
                {
                    CombatTraceEntry[] entries = bridge.Trace.Snapshot();
                    localDamageTraceObserved |= entries.Any(entry =>
                        entry.Kind == CombatTraceKind.DamageResolved &&
                        entry.SourcePlayerId == localPlayerId &&
                        entry.Damage > 0);
                }

                if (localDamageTraceObserved &&
                    !localBuildStoppedAfterDamage && build != null &&
                    build.IsBuildActive)
                {
                    build.ClearBuild();
                    localBuildStoppedAfterDamage = true;
                    Debug.Log(
                        $"{LogPrefix} event=local-build-stopped-after-hit " +
                        $"role={options.Role} player={localPlayerId}");
                }
            }

            NetworkEnemySimulationEndpoint[] players =
                FindObjectsByType<NetworkEnemySimulationEndpoint>(
                    FindObjectsSortMode.None);
            NetworkEnemySimulationAgent[] enemies =
                FindObjectsByType<NetworkEnemySimulationAgent>(
                    FindObjectsSortMode.None);
            currentPlayerCount = players.Length;
            currentEnemyCount = enemies.Length;
            currentEnemiesAlive = enemies.Length > 0 &&
                enemies.All(enemy => enemy.IsCanonicalAlive);

            if (players.Length == 2 && enemies.Length == 2)
            {
                if (!twoPlayerTopologyObserved)
                {
                    twoPlayerTopologyObservedAt = Time.realtimeSinceStartup;
                }
                twoPlayerTopologyObserved = true;
            }

            if (localPlayerId != 0u && players.Length == 2 && enemies.Length == 2)
            {
                uint[] playerIds = players.Select(player => player.netId)
                    .OrderBy(id => id)
                    .ToArray();
                uint[] assignedOwners = enemies
                    .Where(enemy =>
                        enemy.Assignment.Host == EnemySimulationHost.ClientPlayer &&
                        enemy.Assignment.SimulationOwnerPlayerId ==
                            enemy.Assignment.AggroTargetPlayerId)
                    .Select(enemy => enemy.Assignment.SimulationOwnerPlayerId)
                    .OrderBy(id => id)
                    .ToArray();
                assignmentTopologyObserved |=
                    playerIds.SequenceEqual(assignedOwners);

                NetworkEnemySimulationAgent localEnemy = enemies.FirstOrDefault(
                    enemy => enemy.Assignment.SimulationOwnerPlayerId ==
                        localPlayerId);
                NetworkEnemySimulationAgent replicaEnemy = enemies.FirstOrDefault(
                    enemy => enemy.Assignment.SimulationOwnerPlayerId != 0u &&
                        enemy.Assignment.SimulationOwnerPlayerId != localPlayerId);
                localOwnerRoleObserved |= localEnemy != null &&
                    localEnemy.Authority.Role == EnemySimulationRole.ClientOwner;
                replicaRoleObserved |= replicaEnemy != null &&
                    replicaEnemy.Authority.Role == EnemySimulationRole.Replica;
            }

            for (int i = 0; i < enemies.Length; i++)
            {
                CombatantBehaviour combatant =
                    enemies[i].GetComponent<CombatantBehaviour>();
                if (combatant != null &&
                    combatant.CurrentHealth < combatant.MaxHealth)
                {
                    replicatedEnemyDamageObserved = true;
                }
            }
        }

        private void ObserveHostLifecycle()
        {
            if (!hostReadyForLateJoin && IsInitialHostReady())
            {
                hostReadyForLateJoin = true;
                Debug.Log(
                    $"{LogPrefix} event=host-ready-for-late-join " +
                    $"player={localPlayerId}");
            }

            CaptureRemoteIdentity();
            ObserveServerCanonicalCombat();
            if (!preDisconnectObserved && IsConnectedCombatReady())
            {
                preDisconnectObserved = true;
                Debug.Log(
                    $"{LogPrefix} event=pre-disconnect-pass role=Host " +
                    BuildSummary());
            }

            if (preDisconnectObserved && IsHostDisconnectRecovered())
            {
                Finish(true, BuildSummary());
            }
        }

        private void ObserveClientLifecycle()
        {
            remoteEverConnected |= NetworkClient.isConnected;
            if (!disconnectRequested && IsConnectedCombatReady() &&
                twoPlayerTopologyObservedAt >= 0f &&
                Time.realtimeSinceStartup - twoPlayerTopologyObservedAt >=
                    minimumConnectedSeconds)
            {
                preDisconnectObserved = true;
                disconnectRequested = true;
                Debug.Log(
                    $"{LogPrefix} event=pre-disconnect-pass role=Client " +
                    BuildSummary());
                networkManager.StopClient();
                return;
            }

            if (disconnectRequested && !NetworkClient.active &&
                !NetworkClient.isConnected)
            {
                Finish(true, BuildSummary());
            }
        }

        private bool IsInitialHostReady()
        {
            if (!NetworkServer.active || !NetworkClient.active ||
                !networkManager.IsGameplayLoaded ||
                SceneManager.GetActiveScene().path != GameplayScenePath ||
                NetworkClient.localPlayer == null)
            {
                return false;
            }

            NetworkEnemySimulationEndpoint[] players =
                FindObjectsByType<NetworkEnemySimulationEndpoint>(
                    FindObjectsSortMode.None);
            NetworkEnemySimulationAgent[] enemies =
                FindObjectsByType<NetworkEnemySimulationAgent>(
                    FindObjectsSortMode.None);
            return players.Length == 1 && enemies.Length == 1 &&
                localBuildObserved &&
                enemies[0].Assignment.SimulationOwnerPlayerId == localPlayerId &&
                enemies[0].Assignment.AggroTargetPlayerId == localPlayerId &&
                enemies[0].Authority.Role == EnemySimulationRole.ClientOwner;
        }

        private bool IsConnectedCombatReady()
        {
            bool common = networkManager.IsGameplayLoaded &&
                SceneManager.GetActiveScene().path == GameplayScenePath &&
                NetworkClient.localPlayer != null &&
                NetworkClient.localPlayer.gameObject.scene.path ==
                    GameplayScenePath &&
                remoteEverConnected && twoPlayerTopologyObserved &&
                currentPlayerCount == 2 && currentEnemyCount == 2 &&
                currentEnemiesAlive &&
                assignmentTopologyObserved && localBuildObserved &&
                localAutoTargetObserved && localAssignedEnemyTargetObserved &&
                localDamageTraceObserved &&
                replicatedEnemyDamageObserved && localPlayerHealthDecreased &&
                localOwnerRoleObserved && replicaRoleObserved;
            if (options.Role != BootGameplayValidationRole.Host)
            {
                return common;
            }

            NetworkCombatWorld world = NetworkCombatWorld.Instance;
            return common && remotePlayerId != 0u && remoteEnemyId != 0u &&
                serverCanonicalEnemyDamageObserved &&
                serverCanonicalPlayersDamaged && world != null &&
                world.Gateway.Metrics.AcceptedCombatResults >= 2 &&
                world.Gateway.Metrics.AcceptedPlayerReports >= 2;
        }

        private void CaptureRemoteIdentity()
        {
            if (!NetworkServer.active)
            {
                return;
            }

            NetworkEnemySimulationEndpoint[] players =
                FindObjectsByType<NetworkEnemySimulationEndpoint>(
                    FindObjectsSortMode.None);
            for (int i = 0; i < players.Length; i++)
            {
                NetworkConnectionToClient connection =
                    players[i].connectionToClient;
                if (connection != null &&
                    !(connection is LocalConnectionToClient))
                {
                    remotePlayerId = players[i].PlayerEntityId;
                    remoteEverConnected = true;
                    break;
                }
            }

            if (remotePlayerId == 0u)
            {
                return;
            }

            NetworkEnemySimulationAgent[] enemies =
                FindObjectsByType<NetworkEnemySimulationAgent>(
                    FindObjectsSortMode.None);
            for (int i = 0; i < enemies.Length; i++)
            {
                if (enemies[i].Assignment.Host ==
                        EnemySimulationHost.ClientPlayer &&
                    enemies[i].Assignment.SimulationOwnerPlayerId ==
                        remotePlayerId)
                {
                    remoteEnemyId = enemies[i].netId;
                    remoteEnemyAssignmentEpoch = enemies[i].Assignment.Epoch;
                    break;
                }
            }
        }

        private void ObserveServerCanonicalCombat()
        {
            if (!NetworkServer.active || NetworkCombatWorld.Instance == null)
            {
                return;
            }

            CombatLedger ledger = NetworkCombatWorld.Instance.Gateway.Ledger;
            NetworkEnemySimulationAgent[] enemies =
                FindObjectsByType<NetworkEnemySimulationAgent>(
                    FindObjectsSortMode.None);
            for (int i = 0; i < enemies.Length; i++)
            {
                if (ledger.TryGetState(
                        enemies[i].netId,
                        out CanonicalEntityState state) &&
                    state.Health < state.MaxHealth)
                {
                    serverCanonicalEnemyDamageObserved = true;
                }
            }

            if (localPlayerId != 0u && remotePlayerId != 0u &&
                ledger.TryGetState(localPlayerId, out CanonicalEntityState local) &&
                ledger.TryGetState(remotePlayerId, out CanonicalEntityState remote))
            {
                serverCanonicalPlayersDamaged =
                    local.Health < local.MaxHealth &&
                    remote.Health < remote.MaxHealth;
            }
        }

        private bool IsHostDisconnectRecovered()
        {
            if (!NetworkServer.active || !NetworkClient.active ||
                remotePlayerId == 0u || remoteEnemyId == 0u ||
                NetworkServer.connections.Count != 1)
            {
                return false;
            }

            NetworkEnemySimulationEndpoint[] players =
                FindObjectsByType<NetworkEnemySimulationEndpoint>(
                    FindObjectsSortMode.None);
            NetworkEnemySimulationAgent[] enemies =
                FindObjectsByType<NetworkEnemySimulationAgent>(
                    FindObjectsSortMode.None);
            if (players.Length != 1 || players[0].PlayerEntityId != localPlayerId ||
                enemies.Length != 2)
            {
                return false;
            }

            NetworkEnemySimulationAgent recovered = enemies.FirstOrDefault(
                enemy => enemy.netId == remoteEnemyId);
            NetworkEnemySimulationAgent local = enemies.FirstOrDefault(
                enemy => enemy.Assignment.SimulationOwnerPlayerId == localPlayerId);
            NetworkEnemySimulationWorld simulationWorld =
                NetworkEnemySimulationWorld.Instance;
            return recovered != null && local != null &&
                recovered.Assignment.Host ==
                    EnemySimulationHost.ServerFallback &&
                recovered.Assignment.SimulationOwnerPlayerId == 0u &&
                recovered.Assignment.AggroTargetPlayerId == localPlayerId &&
                recovered.Assignment.Epoch > remoteEnemyAssignmentEpoch &&
                recovered.Authority.Role == EnemySimulationRole.ServerFallback &&
                local.Assignment.Host == EnemySimulationHost.ClientPlayer &&
                local.Authority.Role == EnemySimulationRole.ClientOwner &&
                simulationWorld != null &&
                simulationWorld.Registry.TryGetLatestSnapshot(
                    remoteEnemyId,
                    out EnemySimulationSnapshot snapshot) &&
                snapshot.Sequence > 0u;
        }

        private string BuildSummary()
        {
            NetworkEnemySimulationEndpoint[] players =
                FindObjectsByType<NetworkEnemySimulationEndpoint>(
                    FindObjectsSortMode.None);
            NetworkEnemySimulationAgent[] enemies =
                FindObjectsByType<NetworkEnemySimulationAgent>(
                    FindObjectsSortMode.None);
            string summary =
                $"gameplay={networkManager != null && networkManager.IsGameplayLoaded} " +
                $"players={players.Length} enemies={enemies.Length} " +
                $"alive={currentEnemiesAlive} " +
                $"local={localPlayerId} remote={remotePlayerId} " +
                $"remoteEnemy={remoteEnemyId} topology={assignmentTopologyObserved} " +
                $"build={localBuildObserved} autoTarget={localAutoTargetObserved} " +
                $"assignedTarget={localAssignedEnemyTargetObserved} " +
                $"trace={localDamageTraceObserved} enemyHp={replicatedEnemyDamageObserved} " +
                $"buildStopped={localBuildStoppedAfterDamage} " +
                $"localHp={localPlayerHealthDecreased} " +
                $"roles={localOwnerRoleObserved}/{replicaRoleObserved} " +
                $"canonicalEnemy={serverCanonicalEnemyDamageObserved} " +
                $"canonicalPlayers={serverCanonicalPlayersDamaged} " +
                $"disconnect={disconnectRequested}";
            if (options.Role != BootGameplayValidationRole.Host ||
                NetworkCombatWorld.Instance == null)
            {
                return summary;
            }

            CombatGatewayMetrics metrics =
                NetworkCombatWorld.Instance.Gateway.Metrics;
            return summary +
                $" accepted={metrics.AcceptedCombatResults} " +
                $"reports={metrics.AcceptedPlayerReports}";
        }

        private void Finish(bool success, string detail)
        {
            if (finished)
            {
                return;
            }

            finished = true;
            SceneManager.sceneLoaded -= HandleValidationSceneLoaded;
            if (success)
            {
                Debug.Log(
                    $"{LogPrefix} result=PASS role={options.Role} {detail}");
            }
            else
            {
                Debug.LogError(
                    $"{LogPrefix} result=FAIL role={options.Role} {detail}");
            }

            if (!Application.isEditor)
            {
                StartCoroutine(QuitAfterGrace(success ? 0 : 1));
            }
        }

        private IEnumerator QuitAfterGrace(int exitCode)
        {
            if (quitGraceSeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(quitGraceSeconds);
            }
            Application.Quit(exitCode);
        }

        public static bool TryParseOptions(
            string[] arguments,
            out BootGameplayValidationOptions parsed,
            out string error)
        {
            parsed = default;
            error = null;
            string roleText = FindValue(arguments, "--boot-gameplay-role=");
            if (string.IsNullOrEmpty(roleText))
            {
                return false;
            }

            BootGameplayValidationRole role;
            if (string.Equals(roleText, "host", StringComparison.OrdinalIgnoreCase))
            {
                role = BootGameplayValidationRole.Host;
            }
            else if (string.Equals(
                roleText,
                "client",
                StringComparison.OrdinalIgnoreCase))
            {
                role = BootGameplayValidationRole.Client;
            }
            else
            {
                error = $"Unknown --boot-gameplay-role value: {roleText}.";
                return false;
            }

            string address = FindValue(arguments, "--boot-gameplay-address=");
            if (string.IsNullOrWhiteSpace(address))
            {
                address = "127.0.0.1";
            }

            ushort port = 7801;
            string portText = FindValue(arguments, "--boot-gameplay-port=");
            if (!string.IsNullOrEmpty(portText) &&
                !ushort.TryParse(
                    portText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out port))
            {
                error = $"Invalid --boot-gameplay-port value: {portText}.";
                return false;
            }

            float timeout = DefaultTimeoutSeconds;
            string timeoutText = FindValue(arguments, "--boot-gameplay-timeout=");
            if (!string.IsNullOrEmpty(timeoutText) &&
                (!float.TryParse(
                    timeoutText,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out timeout) || timeout <= 0f))
            {
                error = $"Invalid --boot-gameplay-timeout value: {timeoutText}.";
                return false;
            }

            parsed = new BootGameplayValidationOptions(
                role,
                address,
                port,
                timeout);
            return true;
        }

        private static string FindValue(string[] arguments, string prefix)
        {
            if (arguments == null)
            {
                return null;
            }
            for (int i = 0; i < arguments.Length; i++)
            {
                string argument = arguments[i];
                if (argument != null && argument.StartsWith(
                        prefix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return argument.Substring(prefix.Length);
                }
            }
            return null;
        }
    }
}
