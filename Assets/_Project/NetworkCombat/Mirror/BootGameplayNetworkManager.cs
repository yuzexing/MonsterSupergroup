using System.Collections;
using System.Collections.Generic;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MonsterSupergroup.NetworkCombat
{
    [DisallowMultipleComponent]
    public sealed class BootGameplayNetworkManager : NetworkManager
    {
        [Scene]
        [SerializeField] private string gameplayScene = "Assets/Scenes/Gameplay.unity";
        [SerializeField] private Camera bootCamera;
        [SerializeField] private AudioListener bootAudioListener;

        private readonly HashSet<int> pendingPlayerConnections =
            new HashSet<int>();
        private readonly HashSet<int> remoteGameplayLoadRequests =
            new HashSet<int>();
        private bool serverGameplayLoaded;
        private bool gameplayUnloadStarted;
        private AsyncOperation gameplayLoadOperation;
        private AsyncOperation stoppedClientSceneOperation;
        private uint serverSceneGeneration;
        private uint clientSceneGeneration;
        private bool completingClientScene;
        private Scene bootScene;
        private Scene serverGameplayScene;

        public string GameplayScene => gameplayScene;

        public bool IsGameplayLoaded => TryGetGameplayScene(out _);

        public bool IsGameplayTransitioning => gameplayLoadOperation != null || gameplayUnloadStarted ||
            stoppedClientSceneOperation != null || (!NetworkServer.active && loadingSceneAsync != null);

        public RunSession Session { get; private set; } = new RunSession();

        public override void Awake()
        {
            // Install before Mirror registers authentication callbacks. The same boundary
            // is used by Boot, test scenes and programmatic server startup.
            if (authenticator == null)
                authenticator = GetComponent<RunSessionAuthenticator>() ?? gameObject.AddComponent<RunSessionAuthenticator>();
            base.Awake();
        }

        /// <summary>Called by the server's start-run flow after the intended party has joined.</summary>
        public void BeginRun()
        {
            if (!NetworkServer.active) throw new System.InvalidOperationException("Only the server can start a run.");
            Session.BeginRun();
        }

        public void ConfigureGameplay(
            string scenePath,
            Camera sourceBootCamera,
            AudioListener sourceBootAudioListener)
        {
            gameplayScene = !string.IsNullOrWhiteSpace(scenePath)
                ? scenePath
                : throw new System.ArgumentException(
                    "A Gameplay scene path is required.",
                    nameof(scenePath));
            bootCamera = sourceBootCamera;
            bootAudioListener = sourceBootAudioListener;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            uint generation = ++serverSceneGeneration;
            Session = new RunSession();
            if (NetworkCombatWorld.Instance != null)
                NetworkCombatWorld.Instance.ServerCanonicalBatchProduced += UpdateParticipantLife;
            CaptureBootScene();
            serverGameplayLoaded = false;
            serverGameplayScene = default;
            Debug.Log($"[BootGameplay] Server starting; loading '{gameplayScene}'.", this);
            StartCoroutine(ServerLoadGameplay(generation));
        }

        public override void OnStartClient()
        {
            ++clientSceneGeneration;
            base.OnStartClient();
            CaptureBootScene();
        }

        public override void OnClientConnect()
        {
            if (!NetworkServer.active && gameplayUnloadStarted)
                StartCoroutine(ReadyClientAfterCleanup(NetworkClient.connection, clientSceneGeneration));
            else
                base.OnClientConnect();
        }

        private IEnumerator ReadyClientAfterCleanup(NetworkConnectionToServer connection, uint generation)
        {
            while (gameplayUnloadStarted)
            {
                if (generation != clientSceneGeneration || !ReferenceEquals(connection, NetworkClient.connection))
                    yield break;
                yield return null;
            }
            if (generation == clientSceneGeneration && ReferenceEquals(connection, NetworkClient.connection) &&
                NetworkClient.isConnected)
                base.OnClientConnect();
        }

        public override void OnServerReady(NetworkConnectionToClient connection)
        {
            base.OnServerReady(connection);
            Debug.Log(
                $"[BootGameplay] Server connection {connection?.connectionId} is ready.",
                this);

            if (connection != null &&
                !(connection is LocalConnectionToClient) &&
                remoteGameplayLoadRequests.Add(connection.connectionId))
            {
                NetworkServer.SetClientNotReady(connection);
                StartCoroutine(SendGameplaySceneWhenReady(connection));
                return;
            }

            QueuePlayerCreation(connection);
        }

        public override void OnServerAddPlayer(NetworkConnectionToClient connection)
        {
            QueuePlayerCreation(connection);
        }

        public override void OnServerDisconnect(NetworkConnectionToClient connection)
        {
            if (connection != null)
            {
                // Capture before Mirror destroys the avatar and clears its Build/status registries.
                PlayerRuntimeCheckpoint checkpoint = CapturePlayer(connection.identity);
                Session.Disconnect(connection.connectionId, checkpoint);
                pendingPlayerConnections.Remove(connection.connectionId);
                remoteGameplayLoadRequests.Remove(connection.connectionId);
            }

            base.OnServerDisconnect(connection);
        }

        private static PlayerRuntimeCheckpoint CapturePlayer(NetworkIdentity identity)
        {
            if (identity == null) return null;
            var build = identity.GetComponent<PlayerBuildRuntime>();
            var selection = identity.GetComponent<NetworkModifierSelection>();
            NetworkCombatWorld world = NetworkCombatWorld.Instance;
            if (build == null || !build.IsBuildActive || selection == null || world == null ||
                !world.Gateway.Ledger.TryCaptureEntityState(identity.netId, out ServerEntityCheckpoint health))
                return null;
            return new PlayerRuntimeCheckpoint
            {
                PreviousAvatarId = identity.netId,
                CapturedAt = NetworkTime.time,
                Build = build.CaptureState(),
                Progression = selection.CaptureProgression(),
                Health = health,
                WeaponCooldowns = identity.GetComponent<NetworkWeaponCombatAdapter>().CaptureCooldowns(),
                Dash = identity.GetComponent<NetworkPlayerDash>()?.CaptureServerState(),
                Ultimate = identity.GetComponent<NetworkPlayerUltimate>()?.CaptureServerState(),
                SummonMaturities = identity.GetComponent<NetworkWeaponCombatAdapter>().CaptureSummonMaturities(),
                Statuses = world.Gateway.Statuses.CaptureTarget(identity.netId, NetworkTime.time),
                LifeState = health.State.Alive ? RunPlayerLifeState.Active : RunPlayerLifeState.Downed
            };
        }

        private void UpdateParticipantLife(CanonicalWorldBatch batch)
        {
            if (batch.Entities == null) return;
            foreach (var state in batch.Entities)
                foreach (RunParticipant participant in Session.Participants)
                    if (participant.AvatarId == state.EntityId)
                        participant.LifeState = state.Alive ? RunPlayerLifeState.Active : RunPlayerLifeState.Downed;
        }

        public override void OnClientSceneChanged()
        {
            if (!NetworkClient.isConnected || gameplayUnloadStarted) return;
            completingClientScene = true;
            try
            {
                base.OnClientSceneChanged();
                if (TryGetGameplayScene(out Scene scene)) ActivateGameplay(scene);
                else RestoreBootPresentation();
            }
            finally
            {
                completingClientScene = false;
            }
        }

        public override void OnStopServer()
        {
            ++serverSceneGeneration;
            if (NetworkCombatWorld.Instance != null)
                NetworkCombatWorld.Instance.ServerCanonicalBatchProduced -= UpdateParticipantLife;
            Debug.Log("[BootGameplay] Server stopping.", this);
            if (NetworkServer.active && TryGetGameplayScene(out _))
            {
                foreach (NetworkConnectionToClient connection in
                         NetworkServer.connections.Values)
                {
                    if (connection != null &&
                        !(connection is LocalConnectionToClient))
                    {
                        connection.Send(new SceneMessage
                        {
                            sceneName = gameplayScene,
                            sceneOperation = SceneOperation.UnloadAdditive
                        });
                    }
                }
            }

            pendingPlayerConnections.Clear();
            remoteGameplayLoadRequests.Clear();
            BeginGameplayUnload();
            base.OnStopServer();
        }

        public override void OnStopClient()
        {
            ++clientSceneGeneration;
            Debug.Log(
                $"[BootGameplay] Client stopping; serverActive={NetworkServer.active}.",
                this);
            if (!NetworkServer.active)
            {
                // Mirror owns remote SceneMessage operations. Retain the handle
                // for cleanup, but prevent its completion from readying a new connection.
                if (loadingSceneAsync != null) stoppedClientSceneOperation = loadingSceneAsync;
                // During Mirror's completion callback its finally still uses
                // loadingSceneAsync. In that case Mirror will clear it itself.
                if (!completingClientScene) loadingSceneAsync = null;
                BeginGameplayUnload();
            }

            base.OnStopClient();
        }

        private IEnumerator ServerLoadGameplay(uint generation)
        {
            // Unity scene operations cannot be cancelled. A new run waits until
            // the previous run has finished loading and unloading its scene.
            while (IsGameplayTransitioning)
            {
                if (!IsCurrentServerGeneration(generation)) yield break;
                yield return null;
            }
            if (!IsCurrentServerGeneration(generation)) yield break;

            if (TryGetGameplayScene(out Scene loadedScene))
            {
                serverGameplayScene = loadedScene;
                serverGameplayLoaded = true;
                ActivateGameplay(loadedScene);
                Debug.Log(
                    $"[BootGameplay] Reusing loaded Gameplay scene '{loadedScene.path}'.",
                    this);
                yield break;
            }

            if (string.IsNullOrWhiteSpace(gameplayScene))
            {
                Debug.LogError(
                    "BootGameplayNetworkManager requires a Gameplay scene.",
                    this);
                yield break;
            }

            gameplayLoadOperation = SceneManager.LoadSceneAsync(
                gameplayScene,
                LoadSceneMode.Additive);
            if (gameplayLoadOperation == null)
            {
                Debug.LogError(
                    $"Unable to load Gameplay scene '{gameplayScene}'.",
                    this);
                yield break;
            }

            yield return gameplayLoadOperation;
            gameplayLoadOperation = null;
            if (!IsCurrentServerGeneration(generation))
            {
                // Loading cannot be cancelled by Unity. A stop received while it
                // was in flight still owns cleanup when the scene becomes available.
                BeginGameplayUnload();
                yield break;
            }
            if (!TryGetGameplayScene(out loadedScene))
            {
                Debug.LogError(
                    $"Gameplay scene '{gameplayScene}' did not finish loading.",
                    this);
                yield break;
            }

            serverGameplayScene = loadedScene;
            serverGameplayLoaded = true;
            ActivateGameplay(loadedScene);
            Debug.Log(
                $"[BootGameplay] Gameplay scene ready: '{loadedScene.path}'.",
                this);
        }

        private void QueuePlayerCreation(NetworkConnectionToClient connection)
        {
            if (connection == null)
            {
                Debug.LogWarning(
                    "[BootGameplay] Ignored player creation for a null connection.",
                    this);
                return;
            }
            if (connection.identity != null)
            {
                Debug.Log(
                    $"[BootGameplay] Connection {connection.connectionId} already " +
                    "has a Player.",
                    this);
                return;
            }
            if (!pendingPlayerConnections.Add(connection.connectionId))
            {
                Debug.Log(
                    $"[BootGameplay] Connection {connection.connectionId} is already " +
                    "waiting for Gameplay.",
                    this);
                return;
            }

            Debug.Log(
                $"[BootGameplay] Queued Player for connection " +
                $"{connection.connectionId}.",
                this);
            StartCoroutine(AddPlayerWhenGameplayReady(connection));
        }

        private IEnumerator SendGameplaySceneWhenReady(
            NetworkConnectionToClient connection)
        {
            uint generation = serverSceneGeneration;
            while (IsCurrentServerGeneration(generation) && !serverGameplayLoaded)
            {
                yield return null;
            }

            if (!IsCurrentServerGeneration(generation) ||
                !IsCurrentConnection(connection) || connection.identity != null)
            {
                yield break;
            }

            connection.Send(new SceneMessage
            {
                sceneName = gameplayScene,
                sceneOperation = SceneOperation.LoadAdditive
            });
            Debug.Log(
                $"[BootGameplay] Requested Gameplay load for remote connection " +
                $"{connection.connectionId}.",
                this);
        }

        private IEnumerator AddPlayerWhenGameplayReady(
            NetworkConnectionToClient connection)
        {
            int connectionId = connection.connectionId;
            uint generation = serverSceneGeneration;
            while (IsCurrentServerGeneration(generation) && !serverGameplayLoaded)
            {
                yield return null;
            }

            // Disconnect already removes this request. Never remove a new
            // connection's pending entry when Mirror has reused its numeric ID.
            if (!IsCurrentServerGeneration(generation) || !IsCurrentConnection(connection)) yield break;

            if (connection.identity != null ||
                !serverGameplayScene.IsValid() || !serverGameplayScene.isLoaded)
            {
                Debug.LogWarning(
                    $"[BootGameplay] Aborted Player spawn for connection " +
                    $"{connectionId}: current={IsCurrentConnection(connection)}, " +
                    $"hasPlayer={connection.identity != null}, " +
                    $"sceneValid={serverGameplayScene.IsValid()}, " +
                    $"sceneLoaded={serverGameplayScene.isLoaded}.",
                    this);
                pendingPlayerConnections.Remove(connectionId);
                yield break;
            }

            yield return null;
            if (!IsCurrentServerGeneration(generation) || !IsCurrentConnection(connection)) yield break;
            if (connection.identity != null)
            {
                pendingPlayerConnections.Remove(connectionId);
                yield break;
            }

            if (!Session.TryGetConnection(connectionId, out RunParticipant participant))
            {
                // A configured custom authenticator must admit a transport-verified identity;
                // there is intentionally no fallback to netId, address or GameDirector.Player.
                Debug.LogError("[RunSession] Player spawn requires an admitted participant.", this);
                pendingPlayerConnections.Remove(connectionId);
                connection.Disconnect();
                yield break;
            }

            Transform start = GetStartPosition();
            GameObject player = start != null
                ? Instantiate(playerPrefab, start.position, start.rotation)
                : Instantiate(playerPrefab);
            player.name = $"{playerPrefab.name} [connId={connectionId}]";
            SceneManager.MoveGameObjectToScene(player, serverGameplayScene);
            NetworkRunParticipant runtimeParticipant = player.GetComponent<NetworkRunParticipant>();
            if (runtimeParticipant == null)
            {
                Debug.LogError("NetworkPlayer prefab requires NetworkRunParticipant.", player);
                Destroy(player);
                connection.Disconnect();
                pendingPlayerConnections.Remove(connectionId);
                yield break;
            }
            runtimeParticipant.Prepare(Session.RunId, participant);
            if (participant.Checkpoint != null)
            {
                player.GetComponent<NetworkModifierSelection>().PrepareServerRestore(
                    participant.Checkpoint.Build, participant.Checkpoint.Progression);
                player.GetComponent<NetworkCombatantAdapter>().PrepareServerRestore(participant.Checkpoint);
                player.GetComponent<NetworkWeaponCombatAdapter>().PrepareServerRestore(participant.Checkpoint.WeaponCooldowns);
                player.GetComponent<NetworkWeaponCombatAdapter>().PrepareServerSummonRestore(participant.Checkpoint.SummonMaturities);
                if (participant.Checkpoint.Dash.HasValue)
                    player.GetComponent<NetworkPlayerDash>().PrepareServerRestore(participant.Checkpoint.Dash.Value);
                if (participant.Checkpoint.Ultimate.HasValue)
                    player.GetComponent<NetworkPlayerUltimate>().PrepareServerRestore(participant.Checkpoint.Ultimate.Value);
            }
            NetworkServer.AddPlayerForConnection(connection, player);
            Session.AttachAvatar(connectionId, player.GetComponent<NetworkIdentity>().netId,
                player.GetComponent<MirrorNetworkCombatBridge>().ConnectionEpoch);
            pendingPlayerConnections.Remove(connectionId);
            Debug.Log(
                $"[BootGameplay] Spawned Player {player.name} in '{player.scene.path}'.",
                this);
        }

        private static bool IsCurrentConnection(
            NetworkConnectionToClient connection)
        {
            return connection != null && NetworkServer.active &&
                NetworkServer.connections.TryGetValue(
                    connection.connectionId,
                    out NetworkConnectionToClient current) &&
                ReferenceEquals(current, connection);
        }

        private bool IsCurrentServerGeneration(uint generation) =>
            generation == serverSceneGeneration && NetworkServer.active;

        private void ActivateGameplay(Scene scene)
        {
            if (scene.IsValid() && scene.isLoaded)
            {
                SceneManager.SetActiveScene(scene);
            }

            if (bootCamera != null)
            {
                bootCamera.enabled = false;
            }
            if (bootAudioListener != null)
            {
                bootAudioListener.enabled = false;
            }
        }

        private void RestoreBootPresentation()
        {
            if (bootScene.IsValid() && bootScene.isLoaded)
            {
                SceneManager.SetActiveScene(bootScene);
            }

            if (bootCamera != null)
            {
                bootCamera.enabled = !GameplayRuntimeEnvironment.IsDedicatedServer;
            }
            if (bootAudioListener != null)
            {
                bootAudioListener.enabled = !GameplayRuntimeEnvironment.IsDedicatedServer;
            }
        }

        private void CaptureBootScene()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.isLoaded &&
                activeScene.name != "DontDestroyOnLoad" &&
                !IsGameplayScene(activeScene))
            {
                bootScene = activeScene;
            }
        }

        private bool IsGameplayScene(Scene scene)
        {
            return scene.path == gameplayScene ||
                scene.name == System.IO.Path.GetFileNameWithoutExtension(
                    gameplayScene);
        }

        private void BeginGameplayUnload()
        {
            if (gameplayUnloadStarted)
            {
                return;
            }

            gameplayUnloadStarted = true;
            serverGameplayLoaded = false;
            Debug.Log("[BootGameplay] Gameplay unload requested.", this);
            RestoreBootPresentation();
            StartCoroutine(UnloadGameplay());
        }

        private IEnumerator UnloadGameplay()
        {
            // Keep the cleanup gate closed even when Stop arrives before the
            // additive load has made a Scene available to GetSceneByPath.
            while (gameplayLoadOperation != null) yield return null;
            if (stoppedClientSceneOperation != null)
            {
                yield return stoppedClientSceneOperation;
                stoppedClientSceneOperation = null;
            }
            if (TryGetGameplayScene(out Scene scene))
            {
                AsyncOperation operation = SceneManager.UnloadSceneAsync(scene);
                if (operation != null) yield return operation;
            }

            // New server loads are waiting on gameplayUnloadStarted, so this
            // completion cannot reset a scene belonging to the new generation.
            ResetGameplayState();
        }

        private void ResetGameplayState()
        {
            serverGameplayScene = default;
            serverGameplayLoaded = false;
            gameplayUnloadStarted = false;
        }

        private bool TryGetGameplayScene(out Scene scene)
        {
            scene = SceneManager.GetSceneByPath(gameplayScene);
            if (!scene.IsValid())
            {
                scene = SceneManager.GetSceneByName(gameplayScene);
            }
            if (!scene.IsValid())
            {
                string sceneName = System.IO.Path.GetFileNameWithoutExtension(
                    gameplayScene);
                scene = SceneManager.GetSceneByName(sceneName);
            }

            return scene.IsValid() && scene.isLoaded;
        }
    }
}
