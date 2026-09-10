using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using AstralShift.HellMaiden;
using AstralShift.Managers;
using Mirror;
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
    public sealed class BootGameplaySceneLifecycleTests
    {
        private const string BootScenePath = "Assets/_Project/Scenes/Boot.unity";
        private BootGameplayNetworkManager manager;
        private GameObject[] bootRoots;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
                BootScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(BootScenePath, LoadSceneMode.Single);
#endif
            bootRoots = BootSceneFixtureObjects.Capture(BootScenePath);
            manager = Object.FindFirstObjectByType<BootGameplayNetworkManager>();
            Assert.That(manager, Is.Not.Null);
            Assert.That(manager.GetComponent<NetworkBackendBootstrap>().TryPrepareKcp(
                "127.0.0.1", 7898, false, out string error), Is.True, error);
        }

        [UnityTest]
        public IEnumerator StopDuringLoad_ImmediateRestartCreatesOnlyCurrentGameplayScene()
        {
            manager.StartServer();
            string previousRun = manager.Session.RunId;
            Assert.That(manager.IsGameplayTransitioning, Is.True);
            Assert.That(manager.IsGameplayLoaded, Is.False,
                "Stop before the first additive load finishes; no frame has been yielded.");

            manager.StopServer();
            manager.StartServer();
            Assert.That(manager.Session.RunId, Is.Not.EqualTo(previousRun));
            yield return AssertSingleReadyGameplay();
        }

        [UnityTest]
        public IEnumerator StopDuringUnload_ImmediateRestartWaitsForPreviousSceneCleanup()
        {
            manager.StartServer();
            yield return AssertSingleReadyGameplay();
            Scene previousScene = SceneManager.GetSceneByPath(manager.GameplayScene);
            string previousRun = manager.Session.RunId;

            manager.StopServer();
            Assert.That(manager.IsGameplayTransitioning, Is.True);
            manager.StartServer();
            Assert.That(manager.Session.RunId, Is.Not.EqualTo(previousRun));
            yield return AssertSingleReadyGameplay();

            Assert.That(SceneManager.GetSceneByPath(manager.GameplayScene).handle,
                Is.Not.EqualTo(previousScene.handle), "The new run must not reuse a scene being unloaded.");
        }

        [UnityTest]
        public IEnumerator RemoteDisconnectDuringMirrorLoad_UnloadsTheCompletedOrphan()
        {
            var transport = ConfigureClientTransport();
            ConnectClient(transport);
            StartMirrorClientGameplayLoad();
            Assert.That(NetworkManager.loadingSceneAsync, Is.Not.Null);
            manager.StopClient();
            Assert.That(NetworkManager.loadingSceneAsync, Is.Null,
                "The old operation must no longer complete through Mirror's next connection.");
            Assert.That(manager.IsGameplayTransitioning, Is.True);
            yield return WaitForSceneCleanup();
            Assert.That(manager.IsGameplayLoaded, Is.False);
            Assert.That(NetworkClient.active, Is.False);
        }

        [UnityTest]
        public IEnumerator RemoteReconnectDuringMirrorLoad_ReadiesOnlyAfterOldSceneCleanup()
        {
            var transport = ConfigureClientTransport();
            ConnectClient(transport);
            StartMirrorClientGameplayLoad();
            manager.StopClient();
            ConnectClient(transport);
            Assert.That(NetworkClient.ready, Is.False,
                "Authentication may finish during cleanup, but Ready must wait.");
            yield return WaitForSceneCleanup();
            float deadline = Time.realtimeSinceStartup + 3f;
            while (!NetworkClient.ready && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(NetworkClient.ready, Is.True);
            Assert.That(manager.IsGameplayLoaded, Is.False,
                "The old operation must not become the new connection's Gameplay.");

            StartMirrorClientGameplayLoad();
            deadline = Time.realtimeSinceStartup + 15f;
            while ((manager.IsGameplayTransitioning || !manager.IsGameplayLoaded) &&
                   Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(manager.IsGameplayTransitioning, Is.False);
            Assert.That(manager.IsGameplayLoaded, Is.True);
            Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(manager.GameplayScene));
            AssertSingleGameplayScene();
        }

        [UnityTest]
        public IEnumerator RemoteBootReady_DoesNotSendSpawnBatchOrQueueAvatarBeforeGameplayReady()
        {
            manager.StartServer();
            yield return AssertSingleReadyGameplay();
            Assert.That(NetworkServer.spawned.Count, Is.GreaterThan(0), "Exercise an existing World baseline.");
            var connection = new NetworkConnectionToClient(9876) { isAuthenticated = true };
            int spawnStarts = 0, notReady = 0;
            void Observe(NetworkDiagnostics.MessageInfo info)
            {
                if (info.message is ObjectSpawnStartedMessage) spawnStarts++;
                if (info.message is NotReadyMessage) notReady++;
            }
            NetworkDiagnostics.OutMessageEvent += Observe;
            try
            {
                manager.OnServerReady(connection);
                manager.OnServerAddPlayer(connection);
                Assert.That(connection.isReady, Is.False);
                Assert.That(spawnStarts, Is.Zero, "Boot Ready must not start a deferred spawn batch.");
                Assert.That(notReady, Is.EqualTo(1), "Client must be able to Ready again after the additive load.");
                var pending = (HashSet<int>)typeof(BootGameplayNetworkManager).GetField(
                    "pendingPlayerConnections", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
                Assert.That(pending, Has.No.Member(connection.connectionId));
                Assert.That(connection.identity, Is.Null);
            }
            finally { NetworkDiagnostics.OutMessageEvent -= Observe; }
        }

        private SceneLifecycleClientTransport ConfigureClientTransport()
        {
            var transportObject = new GameObject("Scene lifecycle fixture transport");
            transportObject.transform.SetParent(manager.transform);
            var transport = transportObject.AddComponent<SceneLifecycleClientTransport>();
            manager.transport = transport;
            Transport.active = transport;
            return transport;
        }

        private void ConnectClient(SceneLifecycleClientTransport transport)
        {
            manager.StartClient();
            transport.CompleteConnection();
            // Identity validation has separate tests; exercise the actual Mirror
            // post-authentication callback and SceneMessage load implementation.
            manager.authenticator.OnClientAuthenticated.Invoke();
        }

        private void StartMirrorClientGameplayLoad()
        {
            var changeScene = typeof(NetworkManager).GetMethod("ClientChangeScene",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(changeScene, Is.Not.Null);
            changeScene.Invoke(manager, new object[] { manager.GameplayScene, SceneOperation.LoadAdditive, false });
        }

        private IEnumerator WaitForSceneCleanup()
        {
            float deadline = Time.realtimeSinceStartup + 15f;
            while ((manager.IsGameplayTransitioning || manager.IsGameplayLoaded) &&
                   Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(manager.IsGameplayTransitioning, Is.False);
            Assert.That(manager.IsGameplayLoaded, Is.False);
        }

        private IEnumerator AssertSingleReadyGameplay()
        {
            float deadline = Time.realtimeSinceStartup + 15f;
            while ((manager.IsGameplayTransitioning || !manager.IsGameplayLoaded) &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(NetworkServer.active, Is.True);
            Assert.That(manager.IsGameplayTransitioning, Is.False);
            Assert.That(manager.IsGameplayLoaded, Is.True);
            // Give any stale coroutine a chance to publish or clear its result.
            yield return null;
            yield return null;
            Assert.That(manager.IsGameplayTransitioning, Is.False);
            Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(manager.GameplayScene));
            AssertSingleGameplayScene();
        }

        private void AssertSingleGameplayScene()
        {
            int matchingScenes = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).path == manager.GameplayScene) matchingScenes++;
            Assert.That(matchingScenes, Is.EqualTo(1));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (manager == null) yield break;
            if (NetworkServer.active && NetworkClient.active) manager.StopHost();
            else if (NetworkServer.active) manager.StopServer();
            else if (NetworkClient.active) manager.StopClient();

            float deadline = Time.realtimeSinceStartup + 15f;
            while ((manager.IsGameplayTransitioning || manager.IsGameplayLoaded) &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;
            bool sceneCleaned = !manager.IsGameplayTransitioning && !manager.IsGameplayLoaded;
            BootSceneFixtureObjects.Destroy(bootRoots);
            yield return null;
            NetworkManager.ResetStatics();
            Assert.That(sceneCleaned, Is.True, "Gameplay cleanup must finish before the next fixture.");
        }

        private sealed class SceneLifecycleClientTransport : kcp2k.KcpTransport
        {
            private bool connected;
            public override void ClientConnect(string address) => connected = true;
            public override bool ClientConnected() => connected;
            public override void ClientSend(System.ArraySegment<byte> data, int channelId) { }
            public override void ClientEarlyUpdate() { }
            public override void ClientLateUpdate() { }
            public override void ClientDisconnect()
            {
                if (!connected) return;
                connected = false;
                OnClientDisconnected?.Invoke();
            }
            public void CompleteConnection() => OnClientConnected?.Invoke();
        }
    }

    internal static class BootSceneFixtureObjects
    {
        public static GameObject[] Capture(string scenePath)
        {
            var roots = new HashSet<GameObject>(SceneManager.GetSceneByPath(scenePath).GetRootGameObjects());
            // Rewired and Mirror can move these roots into DontDestroyOnLoad
            // during Awake, before the fixture's scene-load await completes.
            if (GameDirector.Instance != null) roots.Add(GameDirector.Instance.transform.root.gameObject);
            if (ControllerManager.Instance != null) roots.Add(ControllerManager.Instance.transform.root.gameObject);
            if (NetworkManager.singleton != null) roots.Add(NetworkManager.singleton.transform.root.gameObject);
            var result = new GameObject[roots.Count];
            roots.CopyTo(result);
            return result;
        }

        public static void Destroy(GameObject[] roots)
        {
            if (roots == null) return;
            foreach (GameObject root in roots) if (root != null) Object.Destroy(root);
        }
    }
}
