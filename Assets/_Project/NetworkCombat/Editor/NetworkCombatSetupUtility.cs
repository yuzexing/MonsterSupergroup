using System;
using kcp2k;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.Local;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static class NetworkCombatSetupUtility
    {
        public const string PlayerPrefabPath =
            "Assets/_Project/Content/NetworkCombat/NetworkPlayer.prefab";
        public const string EnemyPrefabPath =
            "Assets/_Project/Content/NetworkCombat/NetworkEnemy.prefab";
        public const string WorldPrefabPath =
            "Assets/_Project/Content/NetworkCombat/NetworkCombatWorld.prefab";
        public const string SandboxScenePath =
            "Assets/_Project/Scenes/Development/NetworkCombatSandbox.unity";

        private const string LocalPlayerPrefabPath =
            "Assets/_Project/Content/LocalCombat/LocalPlayer.prefab";
        private const string LocalEnemyPrefabPath =
            "Assets/_Project/Content/LocalCombat/LocalEnemy.prefab";

        [MenuItem("Monster Supergroup/Network Combat/Build Validation Sandbox")]
        public static void BuildSandbox()
        {
            try
            {
                EnsureFolder("Assets/_Project/Content", "NetworkCombat");
                GameObject player = BuildPlayerPrefab();
                GameObject enemy = BuildEnemyPrefab();
                GameObject world = BuildWorldPrefab(enemy);
                BuildScene(player, enemy, world);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log(
                    $"Network combat sandbox built: {SandboxScenePath}. " +
                    "Use Mirror HUD to start Host and up to three Clients.");
                ExitBatchMode(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode)
                {
                    ExitBatchMode(1);
                    return;
                }

                throw;
            }
        }

        private static GameObject BuildPlayerPrefab()
        {
            GameObject root = LoadPrefabContents(LocalPlayerPrefabPath);
            try
            {
                root.name = "NetworkPlayer";
                GetOrAdd<NetworkIdentity>(root);
                NetworkTransformReliable networkTransform =
                    GetOrAdd<NetworkTransformReliable>(root);
                networkTransform.syncDirection = SyncDirection.ClientToServer;

                GetOrAdd<MirrorNetworkCombatBridge>(root);
                GetOrAdd<NetworkWeaponCombatAdapter>(root);
                CombatantBehaviour combatant = root.GetComponent<CombatantBehaviour>();
                GetOrAdd<NetworkCombatantAdapter>(root).Configure(
                    combatant,
                    CombatEntityKind.Player,
                    CombatEntityAuthority.OwnerFinal);
                GetOrAdd<NetworkPlayerBootstrap>(root);

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        }

        private static GameObject BuildEnemyPrefab()
        {
            GameObject root = LoadPrefabContents(LocalEnemyPrefabPath);
            try
            {
                root.name = "NetworkEnemy";
                LocalEnemyDeathBehaviour localDeath =
                    root.GetComponent<LocalEnemyDeathBehaviour>();
                if (localDeath != null)
                {
                    UnityEngine.Object.DestroyImmediate(localDeath);
                }

                GetOrAdd<NetworkIdentity>(root);
                NetworkTransformReliable networkTransform =
                    GetOrAdd<NetworkTransformReliable>(root);
                networkTransform.syncDirection = SyncDirection.ServerToClient;
                CombatantBehaviour combatant = root.GetComponent<CombatantBehaviour>();
                GetOrAdd<NetworkCombatantAdapter>(root).Configure(
                    combatant,
                    CombatEntityKind.Enemy,
                    CombatEntityAuthority.ServerCanonical);
                GetOrAdd<NetworkEnemyServerDriver>(root);

                PrefabUtility.SaveAsPrefabAsset(root, EnemyPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
        }

        private static GameObject BuildWorldPrefab(GameObject enemyPrefab)
        {
            var root = new GameObject("NetworkCombatWorld");
            try
            {
                root.AddComponent<NetworkIdentity>();
                root.AddComponent<NetworkCombatWorld>();
                root.AddComponent<NetworkEnemySandboxSpawner>().Configure(
                    enemyPrefab,
                    count: 120,
                    columnCount: 15,
                    cellSpacing: 1.5f,
                    spawnOrigin: new Vector2(-10f, -6f));
                PrefabUtility.SaveAsPrefabAsset(root, WorldPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(WorldPrefabPath);
        }

        private static void BuildScene(
            GameObject playerPrefab,
            GameObject enemyPrefab,
            GameObject worldPrefab)
        {
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);

            var networkRoot = new GameObject("Network Runtime");
            networkRoot.SetActive(false);
            KcpTransport transport = networkRoot.AddComponent<KcpTransport>();
            transport.NoDelay = true;
            transport.Interval = 10;
            LatencySimulation simulation = networkRoot.AddComponent<LatencySimulation>();
            simulation.wrap = transport;
            simulation.latency = 100f;
            simulation.jitter = 0.05f;
            simulation.jitterSpeed = 2f;
            simulation.unreliableLoss = 5f;
            simulation.unreliableScramble = 2f;
            NetworkManager manager = networkRoot.AddComponent<NetworkManager>();
            manager.transport = simulation;
            manager.sendRate = 60;
            manager.maxConnections = 4;
            manager.dontDestroyOnLoad = false;
            manager.runInBackground = true;
            manager.playerPrefab = playerPrefab;
            manager.autoCreatePlayer = true;
            manager.spawnPrefabs.Add(enemyPrefab);
            networkRoot.AddComponent<NetworkManagerHUD>();
            networkRoot.SetActive(true);

            PrefabUtility.InstantiatePrefab(worldPrefab, scene);
            CreateStartPosition(new Vector2(-2f, 0f));
            CreateStartPosition(new Vector2(2f, 0f));
            CreateStartPosition(new Vector2(0f, -2f));
            CreateStartPosition(new Vector2(0f, 2f));

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 10f;
            camera.backgroundColor = new Color(0.04f, 0.04f, 0.06f, 1f);
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, SandboxScenePath))
            {
                throw new InvalidOperationException(
                    $"Failed to save network combat sandbox: {SandboxScenePath}");
            }
        }

        private static void CreateStartPosition(Vector2 position)
        {
            var start = new GameObject($"Player Start {position.x},{position.y}");
            start.transform.position = position;
            start.AddComponent<NetworkStartPosition>();
        }

        private static GameObject LoadPrefabContents(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                throw new InvalidOperationException($"Required prefab is missing: {path}");
            }

            return PrefabUtility.LoadPrefabContents(path);
        }

        private static T GetOrAdd<T>(GameObject gameObject) where T : Component
        {
            T component = gameObject.GetComponent<T>();
            return component != null ? component : gameObject.AddComponent<T>();
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }

        private static void ExitBatchMode(int exitCode)
        {
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }
    }
}
