using System;
using System.Collections;
using System.Globalization;
using AstralShift.HellMaiden.AI;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public enum EnemySimulationValidationRole : byte
    {
        None = 0,
        Host = 1,
        Client = 2
    }

    public readonly struct EnemySimulationValidationOptions
    {
        public EnemySimulationValidationOptions(
            EnemySimulationValidationRole role,
            string address,
            ushort port,
            float timeoutSeconds)
        {
            Role = role;
            Address = address;
            Port = port;
            TimeoutSeconds = timeoutSeconds;
        }

        public EnemySimulationValidationRole Role { get; }
        public string Address { get; }
        public ushort Port { get; }
        public float TimeoutSeconds { get; }
    }

    /// <summary>
    /// Opt-in process-level validation for the NetworkCombat sandbox. It is inert
    /// unless --enemy-sim-role=host|client is present on the command line.
    /// </summary>
    [DefaultExecutionOrder(-20000)]
    [DisallowMultipleComponent]
    public sealed class NetworkEnemyProcessValidationBootstrap : MonoBehaviour
    {
        private const string LogPrefix = "[EnemySimProcessValidation]";
        private const float DefaultTimeoutSeconds = 30f;

        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private NetworkEnemySandboxSpawner sandboxSpawner;
        [SerializeField] private GameObject skeletonPrefab;
        [SerializeField, Min(0f)] private float minimumObservationSeconds = 6f;
        // Both processes must stay alive long enough for the peer to observe the
        // same connected assignment and write its PASS evidence. Development
        // builds produce very verbose Animancer/FSM logs, so a one-second grace
        // period is not enough even though the transport has already converged.
        [SerializeField, Min(0f)] private float quitGraceSeconds = 10f;
        [SerializeField, Min(0.1f)] private float spawnDistance = 3f;

        private EnemySimulationValidationOptions options;
        private NetworkEnemySimulationEndpoint remoteEndpoint;
        private NetworkEnemySimulationAgent skeleton;
        private uint trackedAttackSequence;
        private uint trackedReplicaAttackSequence;
        private Vector2 initialEnemyPosition;
        private bool hasInitialEnemyPosition;
        private bool remoteConnected;
        private bool spawnedByServer;
        private bool enemyObserved;
        private bool expectedRoleObserved;
        private bool snapshotObserved;
        private bool movementObserved;
        private bool ownerSnapshotMovementObserved;
        private bool warningObserved;
        private bool activeObserved;
        private bool recoveryObserved;
        private bool observerReplicaApplied;
        private bool replicaWarningApplied;
        private bool replicaActiveApplied;
        private bool replicaRecoveryApplied;
        private bool playerHealthDecreased;
        private bool canonicalPlayerHealthDecreased;
        private int initialPlayerHealth = -1;
        private int initialCanonicalPlayerHealth = -1;
        private int lastCanonicalPlayerHealth = -1;
        private uint preparedLocalPlayerId;
        private EnemySimulationRole lastObservedRole;
        private bool hasObservedRole;
        private bool finished;
        private float validationStartTime;

        public NetworkManager ConfiguredNetworkManager => networkManager;
        public NetworkEnemySandboxSpawner ConfiguredSandboxSpawner => sandboxSpawner;
        public GameObject SkeletonPrefab => skeletonPrefab;

        public void Configure(
            NetworkManager manager,
            NetworkEnemySandboxSpawner spawner,
            GameObject enemySkeletonPrefab)
        {
            networkManager = manager != null
                ? manager
                : throw new ArgumentNullException(nameof(manager));
            sandboxSpawner = spawner != null
                ? spawner
                : throw new ArgumentNullException(nameof(spawner));
            skeletonPrefab = enemySkeletonPrefab != null
                ? enemySkeletonPrefab
                : throw new ArgumentNullException(nameof(enemySkeletonPrefab));
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

            if (networkManager == null || sandboxSpawner == null ||
                skeletonPrefab == null)
            {
                Finish(false, "Scene validation references are incomplete.");
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
            validationStartTime = Time.realtimeSinceStartup;

            // The validation bootstrap owns the single Skeleton spawn. Disable the
            // normal 120-Enemy stress spawn without changing the product spawner.
            sandboxSpawner.Configure(skeletonPrefab, 0, 1, 1f, Vector2.zero);
            if (options.Role == EnemySimulationValidationRole.Host)
            {
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
                   Time.realtimeSinceStartup - validationStartTime <
                       options.TimeoutSeconds)
            {
                ObserveNetworkState();
                if (HasPassed() &&
                    Time.realtimeSinceStartup - validationStartTime >=
                        minimumObservationSeconds)
                {
                    Finish(true, BuildSummary());
                    yield break;
                }
                yield return null;
            }

            if (!finished)
            {
                Finish(false, BuildSummary());
            }
        }

        private void ObserveNetworkState()
        {
            if (options.Role == EnemySimulationValidationRole.Host)
            {
                ObserveHost();
            }
            else
            {
                remoteConnected |= NetworkClient.isConnected;
            }

            if (NetworkClient.localPlayer != null)
            {
                ObserveLocalPlayer(NetworkClient.localPlayer);
            }

            if (skeleton == null)
            {
                NetworkEnemySimulationAgent[] agents =
                    FindObjectsByType<NetworkEnemySimulationAgent>(
                        FindObjectsSortMode.None);
                for (int i = 0; i < agents.Length; i++)
                {
                    if (agents[i].GetComponent<NetworkEnemyMeleeReplica>() != null)
                    {
                        skeleton = agents[i];
                        enemyObserved = true;
                        if (!hasInitialEnemyPosition)
                        {
                            initialEnemyPosition = skeleton.transform.position;
                            hasInitialEnemyPosition = true;
                        }
                        Debug.Log(
                            $"{LogPrefix} event=enemy-observed role={options.Role} " +
                            $"enemy={skeleton.netId}");
                        break;
                    }
                }
            }

            if (skeleton == null)
            {
                return;
            }

            EnemySimulationRole expectedRole =
                options.Role == EnemySimulationValidationRole.Host
                    ? EnemySimulationRole.Replica
                    : EnemySimulationRole.ClientOwner;
            EnemySimulationRole currentRole = skeleton.Authority.Role;
            if (!hasObservedRole || currentRole != lastObservedRole)
            {
                Debug.Log(
                    $"{LogPrefix} event=simulation-role role={options.Role} " +
                    $"enemy={skeleton.netId} current={currentRole} " +
                    $"assignmentHost={skeleton.Assignment.Host} " +
                    $"owner={skeleton.Assignment.SimulationOwnerPlayerId} " +
                    $"target={skeleton.Assignment.AggroTargetPlayerId} " +
                    $"epoch={skeleton.Assignment.Epoch}");
                lastObservedRole = currentRole;
                hasObservedRole = true;
            }
            expectedRoleObserved |= currentRole == expectedRole;
            if (hasInitialEnemyPosition &&
                ((Vector2)skeleton.transform.position - initialEnemyPosition)
                    .sqrMagnitude > 0.01f)
            {
                movementObserved = true;
            }

            ObserveAttackPresentation();
            ObserveReplicaAttackPresentation();
            if (options.Role == EnemySimulationValidationRole.Host)
            {
                ObserveServerState();
            }
        }

        private void ObserveHost()
        {
            NetworkEnemySimulationEndpoint[] endpoints =
                FindObjectsByType<NetworkEnemySimulationEndpoint>(
                    FindObjectsSortMode.None);
            NetworkEnemySimulationEndpoint localEndpoint = null;
            for (int i = 0; i < endpoints.Length; i++)
            {
                NetworkConnectionToClient connection = endpoints[i].connectionToClient;
                if (connection is LocalConnectionToClient)
                {
                    localEndpoint = endpoints[i];
                }
                else if (connection != null)
                {
                    remoteEndpoint = endpoints[i];
                }
            }

            remoteConnected |= remoteEndpoint != null;
            if (spawnedByServer || remoteEndpoint == null ||
                localEndpoint == null || !NetworkServer.active)
            {
                return;
            }

            Vector2 awayFromHost =
                (Vector2)(remoteEndpoint.transform.position -
                    localEndpoint.transform.position);
            if (awayFromHost.sqrMagnitude <= 0.0001f)
            {
                awayFromHost = Vector2.right;
            }
            Vector2 spawnPosition = (Vector2)remoteEndpoint.transform.position +
                awayFromHost.normalized * spawnDistance;

            NetworkCombatWorld combatWorld = NetworkCombatWorld.Instance;
            if (combatWorld == null ||
                !combatWorld.Gateway.Ledger.TryGetState(
                    remoteEndpoint.PlayerEntityId,
                    out _))
            {
                return;
            }

            GameObject enemy = Instantiate(
                skeletonPrefab,
                spawnPosition,
                Quaternion.identity);
            NetworkServer.Spawn(enemy);
            NetworkEnemySimulationAgent spawnedAgent =
                enemy.GetComponent<NetworkEnemySimulationAgent>();
            NetworkEnemySimulationWorld simulationWorld =
                NetworkEnemySimulationWorld.Instance;
            if (spawnedAgent == null || simulationWorld == null)
            {
                Finish(false, "Spawned Skeleton lacks Enemy Simulation components.");
                return;
            }
            EnemySimulationAssignment assignment =
                simulationWorld.Registry.AssignClientOwner(
                    spawnedAgent.netId,
                    remoteEndpoint.PlayerEntityId,
                    remoteEndpoint.PlayerEntityId);
            spawnedAgent.SetServerAssignment(assignment);
            spawnedByServer = true;
            initialEnemyPosition = spawnPosition;
            hasInitialEnemyPosition = true;
            Debug.Log(
                $"{LogPrefix} event=server-spawn enemy={enemy.GetComponent<NetworkIdentity>().netId} " +
                $"owner={assignment.SimulationOwnerPlayerId} " +
                $"target={assignment.AggroTargetPlayerId} " +
                $"epoch={assignment.Epoch} position={spawnPosition}");
        }

        private void ObserveLocalPlayer(NetworkIdentity localPlayer)
        {
            CombatantBehaviour combatant =
                localPlayer.GetComponent<CombatantBehaviour>();
            if (combatant == null)
            {
                return;
            }
            if (initialPlayerHealth < 0)
            {
                initialPlayerHealth = combatant.CurrentHealth;
                MirrorNetworkCombatBridge bridge =
                    localPlayer.GetComponent<MirrorNetworkCombatBridge>();
                Debug.Log(
                    $"{LogPrefix} event=local-player role={options.Role} " +
                    $"entity={localPlayer.netId} owner={bridge?.OwnerPlayerId ?? 0u} " +
                    $"health={initialPlayerHealth} version={combatant.StateVersion}");
            }
            if (preparedLocalPlayerId != localPlayer.netId)
            {
                localPlayer.GetComponent<PlayerBuildRuntime>()?.ClearBuild();
                preparedLocalPlayerId = localPlayer.netId;
                Debug.Log(
                    $"{LogPrefix} event=local-weapons-disabled " +
                    $"role={options.Role} entity={localPlayer.netId}");
            }
            bool decreased = combatant.CurrentHealth < initialPlayerHealth;
            if (!playerHealthDecreased && decreased)
            {
                Debug.Log(
                    $"{LogPrefix} event=local-health-decreased role={options.Role} " +
                    $"entity={localPlayer.netId} health={combatant.CurrentHealth} " +
                    $"version={combatant.StateVersion}");
            }
            playerHealthDecreased |= decreased;
        }

        private void ObserveAttackPresentation()
        {
            EnemyAttackPresentationEdge edge = default;
            bool hasEdge;
            if (options.Role == EnemySimulationValidationRole.Host)
            {
                NetworkEnemySimulationWorld world =
                    NetworkEnemySimulationWorld.Instance;
                hasEdge = world != null && world.Registry != null &&
                    world.Registry.TryGetLatestAttackPresentation(
                        skeleton.netId,
                        out edge);
            }
            else
            {
                hasEdge = skeleton.HasLatestAttackPresentation;
                edge = hasEdge ? skeleton.LatestAttackPresentation : default;
            }

            if (!hasEdge || edge.StateSequence == trackedAttackSequence)
            {
                return;
            }

            trackedAttackSequence = edge.StateSequence;
            warningObserved |= edge.Phase == EnemyAttackPresentationPhase.Warning;
            activeObserved |= edge.Phase == EnemyAttackPresentationPhase.Active;
            recoveryObserved |= edge.Phase == EnemyAttackPresentationPhase.Recovery;
            Debug.Log(
                $"{LogPrefix} event=attack-edge role={options.Role} " +
                $"enemy={edge.EnemyEntityId} sequence={edge.StateSequence} " +
                $"phase={edge.Phase}");
        }

        private void ObserveReplicaAttackPresentation()
        {
            if (options.Role != EnemySimulationValidationRole.Host ||
                skeleton.Authority.Role != EnemySimulationRole.Replica)
            {
                return;
            }

            NetworkEnemyMeleeReplica replica =
                skeleton.GetComponent<NetworkEnemyMeleeReplica>();
            if (replica == null || replica.LastAppliedSequence == 0u ||
                replica.LastAppliedSequence == trackedReplicaAttackSequence ||
                replica.LastAppliedAssignmentEpoch != skeleton.Assignment.Epoch)
            {
                return;
            }

            trackedReplicaAttackSequence = replica.LastAppliedSequence;
            replicaWarningApplied |=
                replica.LastAppliedPhase ==
                    EnemyAttackPresentationPhase.Warning;
            replicaActiveApplied |=
                replica.LastAppliedPhase ==
                    EnemyAttackPresentationPhase.Active;
            replicaRecoveryApplied |=
                replica.LastAppliedPhase ==
                    EnemyAttackPresentationPhase.Recovery;
            observerReplicaApplied = replicaWarningApplied &&
                replicaActiveApplied && replicaRecoveryApplied;
            Debug.Log(
                $"{LogPrefix} event=replica-edge-applied enemy={skeleton.netId} " +
                $"epoch={replica.LastAppliedAssignmentEpoch} " +
                $"sequence={replica.LastAppliedSequence} " +
                $"phase={replica.LastAppliedPhase}");
        }

        private void ObserveServerState()
        {
            NetworkEnemySimulationWorld simulationWorld =
                NetworkEnemySimulationWorld.Instance;
            if (simulationWorld != null && simulationWorld.Registry != null &&
                simulationWorld.Registry.TryGetLatestSnapshot(
                    skeleton.netId,
                    out EnemySimulationSnapshot snapshot))
            {
                snapshotObserved |= snapshot.Sequence > 0u;
                bool movedByClientOwner =
                    skeleton.Assignment.Host ==
                        EnemySimulationHost.ClientPlayer &&
                    snapshot.Sequence > 0u && hasInitialEnemyPosition &&
                    (snapshot.Position - initialEnemyPosition).sqrMagnitude >
                        0.01f;
                if (!ownerSnapshotMovementObserved && movedByClientOwner)
                {
                    Debug.Log(
                        $"{LogPrefix} event=owner-snapshot-movement " +
                        $"enemy={skeleton.netId} sequence={snapshot.Sequence} " +
                        $"position={snapshot.Position}");
                }
                ownerSnapshotMovementObserved |= movedByClientOwner;
                movementObserved |= movedByClientOwner;
            }

            if (remoteEndpoint == null)
            {
                return;
            }
            NetworkCombatWorld combatWorld = NetworkCombatWorld.Instance;
            if (combatWorld != null &&
                combatWorld.Gateway.Ledger.TryGetState(
                    remoteEndpoint.PlayerEntityId,
                    out CanonicalEntityState state))
            {
                if (state.Health != lastCanonicalPlayerHealth)
                {
                    lastCanonicalPlayerHealth = state.Health;
                    Debug.Log(
                        $"{LogPrefix} event=remote-canonical-health " +
                        $"entity={state.EntityId} health={state.Health} " +
                        $"version={state.StateVersion}");
                }
                if (initialCanonicalPlayerHealth < 0 &&
                    state.StateVersion > 1u && state.Health == state.MaxHealth)
                {
                    initialCanonicalPlayerHealth = state.Health;
                    Debug.Log(
                        $"{LogPrefix} event=remote-canonical-baseline " +
                        $"entity={state.EntityId} health={state.Health} " +
                        $"version={state.StateVersion}");
                }
                canonicalPlayerHealthDecreased |=
                    initialCanonicalPlayerHealth >= 0 &&
                    state.Health < initialCanonicalPlayerHealth;
            }
        }

        private bool HasPassed()
        {
            EnemySimulationRole expectedRole =
                options.Role == EnemySimulationValidationRole.Host
                    ? EnemySimulationRole.Replica
                    : EnemySimulationRole.ClientOwner;
            bool historicalCommon = remoteConnected && enemyObserved &&
                expectedRoleObserved && movementObserved && warningObserved &&
                activeObserved && recoveryObserved;
            if (options.Role == EnemySimulationValidationRole.Host)
            {
                // Host validates transport events, not a same-frame barrier with
                // the Client process. ownerSnapshotMovementObserved can only be
                // set while the assignment host is ClientPlayer, and the replica
                // flag can only be set by the observing Host. ServerFallback
                // after a disconnect therefore cannot complete this evidence.
                return historicalCommon && spawnedByServer && snapshotObserved &&
                    ownerSnapshotMovementObserved && observerReplicaApplied &&
                    canonicalPlayerHealthDecreased;
            }

            // The simulating Client must still own the Enemy when it passes.
            return historicalCommon && NetworkClient.isConnected &&
                skeleton != null && skeleton.Authority.Role == expectedRole &&
                playerHealthDecreased;
        }

        private string BuildSummary()
        {
            EnemySimulationRole expectedRole =
                options.Role == EnemySimulationValidationRole.Host
                    ? EnemySimulationRole.Replica
                    : EnemySimulationRole.ClientOwner;
            bool peerCurrentlyConnected =
                options.Role == EnemySimulationValidationRole.Host
                    ? remoteEndpoint != null
                    : NetworkClient.isConnected;
            EnemySimulationRole currentRole = skeleton != null
                ? skeleton.Authority.Role
                : EnemySimulationRole.Frozen;
            string summary =
                $"remote={remoteConnected} spawned={spawnedByServer} " +
                $"enemy={enemyObserved} role={expectedRoleObserved} " +
                $"peerNow={peerCurrentlyConnected} " +
                $"roleNow={currentRole} roleExpected={expectedRole} " +
                $"snapshot={snapshotObserved} movement={movementObserved} " +
                $"ownerMovement={ownerSnapshotMovementObserved} " +
                $"warning={warningObserved} active={activeObserved} " +
                $"recovery={recoveryObserved} replica={observerReplicaApplied} " +
                $"replicaPhases={replicaWarningApplied}/" +
                $"{replicaActiveApplied}/{replicaRecoveryApplied} " +
                $"localHp={playerHealthDecreased} " +
                $"canonicalHp={canonicalPlayerHealthDecreased}";
            if (options.Role != EnemySimulationValidationRole.Host ||
                NetworkCombatWorld.Instance == null)
            {
                return summary;
            }

            CombatGatewayMetrics metrics =
                NetworkCombatWorld.Instance.Gateway.Metrics;
            return summary +
                $" reports={metrics.AcceptedPlayerReports}/" +
                $"{metrics.ReceivedPlayerReports}" +
                $" rejectInvalidSender=" +
                metrics.GetRejected(CombatRejectionReason.InvalidSender) +
                $" rejectInvalidSequence=" +
                metrics.GetRejected(CombatRejectionReason.InvalidSequence) +
                $" rejectWrongAuthority=" +
                metrics.GetRejected(CombatRejectionReason.WrongAuthority) +
                $" rejectStale=" +
                metrics.GetRejected(CombatRejectionReason.StaleOwnerReport) +
                $" rejectTargetMissing=" +
                metrics.GetRejected(CombatRejectionReason.TargetNotFound);
        }

        private void Finish(bool success, string detail)
        {
            if (finished)
            {
                return;
            }

            finished = true;
            if (success)
            {
                Debug.Log($"{LogPrefix} result=PASS role={options.Role} {detail}");
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
            out EnemySimulationValidationOptions parsed,
            out string error)
        {
            parsed = default;
            error = null;
            string roleText = FindValue(arguments, "--enemy-sim-role=");
            if (string.IsNullOrEmpty(roleText))
            {
                return false;
            }

            EnemySimulationValidationRole role;
            if (string.Equals(roleText, "host", StringComparison.OrdinalIgnoreCase))
            {
                role = EnemySimulationValidationRole.Host;
            }
            else if (string.Equals(
                roleText,
                "client",
                StringComparison.OrdinalIgnoreCase))
            {
                role = EnemySimulationValidationRole.Client;
            }
            else
            {
                error = $"Unknown --enemy-sim-role value: {roleText}.";
                return false;
            }

            string address = FindValue(arguments, "--enemy-sim-address=");
            if (string.IsNullOrWhiteSpace(address))
            {
                address = "127.0.0.1";
            }

            ushort port = 7798;
            string portText = FindValue(arguments, "--enemy-sim-port=");
            if (!string.IsNullOrEmpty(portText) &&
                !ushort.TryParse(
                    portText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out port))
            {
                error = $"Invalid --enemy-sim-port value: {portText}.";
                return false;
            }

            float timeout = DefaultTimeoutSeconds;
            string timeoutText = FindValue(arguments, "--enemy-sim-timeout=");
            if (!string.IsNullOrEmpty(timeoutText) &&
                (!float.TryParse(
                    timeoutText,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out timeout) || timeout <= 0f))
            {
                error = $"Invalid --enemy-sim-timeout value: {timeoutText}.";
                return false;
            }

            parsed = new EnemySimulationValidationOptions(
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
                if (arguments[i] != null &&
                    arguments[i].StartsWith(
                        prefix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return arguments[i].Substring(prefix.Length);
                }
            }
            return null;
        }
    }
}
