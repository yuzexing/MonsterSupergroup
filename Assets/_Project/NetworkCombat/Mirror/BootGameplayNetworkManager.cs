using System.Collections;
using System.Collections.Generic;
using Mirror;
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
        private Scene bootScene;
        private Scene serverGameplayScene;

        public string GameplayScene => gameplayScene;

        public bool IsGameplayLoaded => TryGetGameplayScene(out _);

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
            CaptureBootScene();
            gameplayUnloadStarted = false;
            Debug.Log($"[BootGameplay] Server starting; loading '{gameplayScene}'.", this);
            StartCoroutine(ServerLoadGameplay());
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            CaptureBootScene();
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
                pendingPlayerConnections.Remove(connection.connectionId);
                remoteGameplayLoadRequests.Remove(connection.connectionId);
            }

            base.OnServerDisconnect(connection);
        }

        public override void OnClientSceneChanged()
        {
            base.OnClientSceneChanged();
            if (TryGetGameplayScene(out Scene scene))
            {
                ActivateGameplay(scene);
            }
            else
            {
                RestoreBootPresentation();
            }
        }

        public override void OnStopServer()
        {
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
            Debug.Log(
                $"[BootGameplay] Client stopping; serverActive={NetworkServer.active}.",
                this);
            if (!NetworkServer.active)
            {
                BeginGameplayUnload();
            }

            base.OnStopClient();
        }

        private IEnumerator ServerLoadGameplay()
        {
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

            AsyncOperation operation = SceneManager.LoadSceneAsync(
                gameplayScene,
                LoadSceneMode.Additive);
            if (operation == null)
            {
                Debug.LogError(
                    $"Unable to load Gameplay scene '{gameplayScene}'.",
                    this);
                yield break;
            }

            yield return operation;
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
            while (NetworkServer.active && !serverGameplayLoaded)
            {
                yield return null;
            }

            if (!IsCurrentConnection(connection) || connection.identity != null)
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
            while (NetworkServer.active && !serverGameplayLoaded)
            {
                yield return null;
            }

            if (!IsCurrentConnection(connection) || connection.identity != null ||
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
            if (!IsCurrentConnection(connection) || connection.identity != null)
            {
                pendingPlayerConnections.Remove(connectionId);
                yield break;
            }

            Transform start = GetStartPosition();
            GameObject player = start != null
                ? Instantiate(playerPrefab, start.position, start.rotation)
                : Instantiate(playerPrefab);
            player.name = $"{playerPrefab.name} [connId={connectionId}]";
            SceneManager.MoveGameObjectToScene(player, serverGameplayScene);
            NetworkServer.AddPlayerForConnection(connection, player);
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
                bootCamera.enabled = true;
            }
            if (bootAudioListener != null)
            {
                bootAudioListener.enabled = true;
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
            Debug.Log("[BootGameplay] Gameplay unload requested.", this);
            RestoreBootPresentation();
            if (TryGetGameplayScene(out Scene scene))
            {
                StartCoroutine(UnloadGameplay(scene));
            }
            else
            {
                ResetGameplayState();
            }
        }

        private IEnumerator UnloadGameplay(Scene scene)
        {
            AsyncOperation operation = SceneManager.UnloadSceneAsync(scene);
            if (operation != null)
            {
                yield return operation;
            }

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
