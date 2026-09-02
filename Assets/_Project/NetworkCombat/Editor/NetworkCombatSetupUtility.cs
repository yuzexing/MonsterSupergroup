using System;
using System.Linq;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Data.Cards;
using kcp2k;
using Mirror;
using Mirror.FizzySteam;
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
        public const string ProductEnemyPrefabPath =
            "Assets/_Project/Content/NetworkCombat/NetworkEnemyBase.prefab";
        public const string WorldPrefabPath =
            "Assets/_Project/Content/NetworkCombat/NetworkCombatWorld.prefab";
        public const string SandboxScenePath =
            "Assets/_Project/Scenes/Development/NetworkCombatSandbox.unity";
        public const string BootScenePath = "Assets/Scenes/Boot.unity";
        public const string GameplayScenePath = "Assets/Scenes/Gameplay.unity";

        private const string NetworkPlayerStartsRootName =
            "Network Player Starts";
        private const string WeaponDatabasePath =
            "Assets/_Project/Content/HellMaiden/NativeGAS/NativeGasWeaponDB.asset";

        [MenuItem("Monster Supergroup/Network Combat/Configure Boot Gameplay Loop")]
        public static void ConfigureBootGameplayLoop()
        {
            try
            {
                BuildBootGameplayAssets();
                Debug.Log(
                    "Boot -> Gameplay network combat loop assets configured.");
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

        public static void BuildBootGameplayAssets()
        {
            BuildSandboxAssets();
            ConfigureProductScenes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        [MenuItem("Monster Supergroup/Network Combat/Build Validation Sandbox")]
        public static void BuildSandbox()
        {
            try
            {
                BuildSandboxAssets();
                Debug.Log(
                    $"Network combat sandbox built: {SandboxScenePath}. " +
                    "Skeleton melee validation and combat pooling are included. " +
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

        public static void BuildSandboxAssets()
        {
            EnsureFolder("Assets/_Project/Content", "NetworkCombat");
            PlayerRuntimeCombatPrefabMigrator.Run();
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(
                PlayerPrefabPath);
            if (player == null)
            {
                throw new InvalidOperationException(
                    $"Player runtime combat repair could not load {PlayerPrefabPath}.");
            }
            GameObject enemy = LoadPrefabAsset(EnemyPrefabPath);
            GameObject productEnemy = LoadPrefabAsset(ProductEnemyPrefabPath);
            GameObject world = LoadPrefabAsset(WorldPrefabPath);
            BuildScene(player, enemy, productEnemy, world);
            EnemySimulationPrefabMigrator.Migrate();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void BuildScene(
            GameObject playerPrefab,
            GameObject enemyPrefab,
            GameObject productEnemyPrefab,
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
            manager.spawnPrefabs.Add(productEnemyPrefab);
            networkRoot.AddComponent<NetworkManagerHUD>();
            networkRoot.SetActive(true);

            GameObject worldInstance = (GameObject)PrefabUtility.InstantiatePrefab(
                worldPrefab,
                scene);
            WeaponDB weaponDatabase = AssetDatabase.LoadAssetAtPath<WeaponDB>(
                WeaponDatabasePath);
            if (weaponDatabase == null)
            {
                throw new InvalidOperationException(
                    $"Required native weapon database is missing: {WeaponDatabasePath}");
            }
            worldInstance.AddComponent<RuntimeDB>()
                .ConfigureWeaponDatabase(weaponDatabase);
            worldInstance.AddComponent<NetworkEnemySandboxSpawner>().Configure(
                enemyPrefab,
                count: 120,
                columnCount: 15,
                cellSpacing: 1.5f,
                spawnOrigin: new Vector2(-10f, -6f));
            CreateStartPosition(new Vector2(-2f, 0f));
            CreateStartPosition(new Vector2(2f, 0f));
            CreateStartPosition(new Vector2(0f, -2f));
            CreateStartPosition(new Vector2(0f, 2f));

            new GameObject("Enemy AI Manager").AddComponent<EnemyAIManager>();

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

        private static void ConfigureProductScenes()
        {
            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PlayerPrefabPath);
            GameObject lightweightEnemy = AssetDatabase.LoadAssetAtPath<GameObject>(
                EnemyPrefabPath);
            GameObject productEnemy = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProductEnemyPrefabPath);
            GameObject skeletonEnemy = AssetDatabase.LoadAssetAtPath<GameObject>(
                EnemySimulationPrefabMigrator.SkeletonNetworkPrefabPath);
            GameObject worldPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                WorldPrefabPath);
            if (playerPrefab == null || lightweightEnemy == null ||
                productEnemy == null || skeletonEnemy == null || worldPrefab == null)
            {
                throw new InvalidOperationException(
                    "Boot Gameplay configuration requires all NetworkCombat prefabs.");
            }

            ConfigureBootScene(
                playerPrefab,
                lightweightEnemy,
                productEnemy,
                skeletonEnemy,
                worldPrefab);
            ConfigureGameplayScene(skeletonEnemy);
        }

        private static void ConfigureBootScene(
            GameObject playerPrefab,
            GameObject lightweightEnemy,
            GameObject productEnemy,
            GameObject skeletonEnemy,
            GameObject worldPrefab)
        {
            Scene scene = EditorSceneManager.OpenScene(
                BootScenePath,
                OpenSceneMode.Single);
            DisableLegacyBootSceneLoader(scene);
            NetworkManager existing = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<NetworkManager>(true))
                .Single();
            GameObject managerObject = existing.gameObject;
            NetworkManagerHUD existingHud =
                managerObject.GetComponent<NetworkManagerHUD>();
            if (!(existing is BootGameplayNetworkManager))
            {
                if (existingHud != null)
                {
                    UnityEngine.Object.DestroyImmediate(existingHud);
                }
                UnityEngine.Object.DestroyImmediate(existing);
            }

            BootGameplayNetworkManager manager =
                managerObject.GetComponent<BootGameplayNetworkManager>();
            if (manager == null)
            {
                manager = managerObject.AddComponent<BootGameplayNetworkManager>();
            }

            KcpTransport kcp = GetOrAdd<KcpTransport>(managerObject);
            kcp.NoDelay = true;
            kcp.Interval = 10;
            kcp.enabled = false;
            LatencySimulation validationTransport =
                GetOrAdd<LatencySimulation>(managerObject);
            validationTransport.wrap = kcp;
            validationTransport.latency = 100f;
            validationTransport.jitter = 0.05f;
            validationTransport.jitterSpeed = 2f;
            validationTransport.unreliableLoss = 5f;
            validationTransport.unreliableScramble = 2f;
            validationTransport.enabled = false;

            FizzySteamworks fizzy = GetOrAdd<FizzySteamworks>(managerObject);
            fizzy.Timeout = 25;
            fizzy.AllowSteamRelay = true;
            fizzy.UseNextGenSteamNetworking = true;
            fizzy.enabled = false;

            manager.transport = fizzy;
            manager.sendRate = 60;
            manager.maxConnections = 4;
            manager.dontDestroyOnLoad = false;
            manager.runInBackground = true;
            manager.offlineScene = string.Empty;
            manager.onlineScene = string.Empty;
            manager.playerPrefab = playerPrefab;
            manager.autoCreatePlayer = false;
            manager.playerSpawnMethod = PlayerSpawnMethod.RoundRobin;
            manager.spawnPrefabs.Clear();
            manager.spawnPrefabs.Add(lightweightEnemy);
            manager.spawnPrefabs.Add(productEnemy);
            manager.spawnPrefabs.Add(skeletonEnemy);
            NetworkManagerHUD[] networkManagerHuds =
                managerObject.GetComponents<NetworkManagerHUD>();
            for (int i = 0; i < networkManagerHuds.Length; i++)
            {
                UnityEngine.Object.DestroyImmediate(networkManagerHuds[i]);
            }

            NetworkBackendBootstrap backendBootstrap =
                GetOrAdd<NetworkBackendBootstrap>(managerObject);
            backendBootstrap.Configure(
                manager,
                fizzy,
                kcp,
                validationTransport);

            KcpLocalNetworkService kcpService =
                GetOrAdd<KcpLocalNetworkService>(managerObject);
            kcpService.Configure(backendBootstrap, manager);
            KcpLocalNetworkHud kcpHud =
                GetOrAdd<KcpLocalNetworkHud>(managerObject);
            kcpHud.Configure(kcpService);

            SteamLobbyService lobbyService =
                GetOrAdd<SteamLobbyService>(managerObject);
            lobbyService.Configure(backendBootstrap, manager, fizzy);
            SteamLobbyHud lobbyHud = GetOrAdd<SteamLobbyHud>(managerObject);
            lobbyHud.Configure(lobbyService);

            Camera bootCamera = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Camera>(true))
                .FirstOrDefault();
            AudioListener bootAudio = bootCamera != null
                ? bootCamera.GetComponent<AudioListener>()
                : null;
            manager.ConfigureGameplay(
                GameplayScenePath,
                bootCamera,
                bootAudio);
            BootGameplayProcessValidationBootstrap processValidation =
                GetOrAdd<BootGameplayProcessValidationBootstrap>(managerObject);
            processValidation.Configure(
                manager,
                validationTransport,
                backendBootstrap);
            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(kcp);
            EditorUtility.SetDirty(validationTransport);
            EditorUtility.SetDirty(fizzy);
            EditorUtility.SetDirty(backendBootstrap);
            EditorUtility.SetDirty(kcpService);
            EditorUtility.SetDirty(kcpHud);
            EditorUtility.SetDirty(lobbyService);
            EditorUtility.SetDirty(lobbyHud);
            EditorUtility.SetDirty(processValidation);

            NetworkCombatWorld[] worlds = scene.GetRootGameObjects()
                .SelectMany(root =>
                    root.GetComponentsInChildren<NetworkCombatWorld>(true))
                .ToArray();
            GameObject worldObject;
            if (worlds.Length == 0)
            {
                worldObject = (GameObject)PrefabUtility.InstantiatePrefab(
                    worldPrefab,
                    scene);
                worldObject.name = "Network Combat World";
            }
            else
            {
                worldObject = worlds[0].gameObject;
                for (int i = 1; i < worlds.Length; i++)
                {
                    UnityEngine.Object.DestroyImmediate(worlds[i].gameObject);
                }
            }

            RemoveIfPresent<NetworkEnemySandboxSpawner>(worldObject);
            RemoveIfPresent<RuntimeDB>(worldObject);
            GetOrAdd<NetworkEnemySimulationWorld>(worldObject);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, BootScenePath))
            {
                throw new InvalidOperationException("Failed to save Boot scene.");
            }
        }

        private static void DisableLegacyBootSceneLoader(Scene scene)
        {
            MonoBehaviour[] behaviours = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true))
                .ToArray();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null)
                {
                    continue;
                }

                MonoScript script = MonoScript.FromMonoBehaviour(behaviour);
                if (script == null || script.name != "SystemController")
                {
                    continue;
                }

                behaviour.enabled = false;
                EditorUtility.SetDirty(behaviour);
            }
        }

        private static void ConfigureGameplayScene(GameObject skeletonEnemy)
        {
            Scene scene = EditorSceneManager.OpenScene(
                GameplayScenePath,
                OpenSceneMode.Single);
            NetworkManager[] managers = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<NetworkManager>(true))
                .ToArray();
            for (int i = 0; i < managers.Length; i++)
            {
                UnityEngine.Object.DestroyImmediate(managers[i]);
            }

            NetworkCombatWorld[] worlds = scene.GetRootGameObjects()
                .SelectMany(root =>
                    root.GetComponentsInChildren<NetworkCombatWorld>(true))
                .ToArray();
            for (int i = 0; i < worlds.Length; i++)
            {
                UnityEngine.Object.DestroyImmediate(worlds[i].gameObject);
            }

            NetworkGameplayEnemySpawner spawner = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    NetworkGameplayEnemySpawner>(true))
                .FirstOrDefault();
            if (spawner == null)
            {
                spawner = new GameObject("Network Gameplay Enemy Spawner")
                    .AddComponent<NetworkGameplayEnemySpawner>();
            }
            spawner.Configure(skeletonEnemy, 5f);
            EditorUtility.SetDirty(spawner);

            GameObject[] startsRoots = scene.GetRootGameObjects()
                .Where(root => root.name == NetworkPlayerStartsRootName)
                .ToArray();
            for (int i = 0; i < startsRoots.Length; i++)
            {
                UnityEngine.Object.DestroyImmediate(startsRoots[i]);
            }

            NetworkStartPosition[] starts = scene.GetRootGameObjects()
                .SelectMany(root =>
                    root.GetComponentsInChildren<NetworkStartPosition>(true))
                .ToArray();
            for (int i = 0; i < starts.Length; i++)
            {
                UnityEngine.Object.DestroyImmediate(starts[i].gameObject);
            }

            var startsRoot = new GameObject(NetworkPlayerStartsRootName);
            CreateStartPosition(startsRoot.transform, new Vector2(-2f, 0f));
            CreateStartPosition(startsRoot.transform, new Vector2(2f, 0f));
            CreateStartPosition(startsRoot.transform, new Vector2(0f, -2f));
            CreateStartPosition(startsRoot.transform, new Vector2(0f, 2f));

            if (!scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<EnemyAIManager>(true))
                .Any())
            {
                new GameObject("Enemy AI Manager").AddComponent<EnemyAIManager>();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, GameplayScenePath))
            {
                throw new InvalidOperationException("Failed to save Gameplay scene.");
            }
        }

        private static void CreateStartPosition(
            Transform parent,
            Vector2 position)
        {
            var start = new GameObject($"Player Start {position.x},{position.y}");
            start.transform.SetParent(parent, false);
            start.transform.position = position;
            start.AddComponent<NetworkStartPosition>();
        }

        private static GameObject LoadPrefabAsset(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                throw new InvalidOperationException($"Required prefab is missing: {path}");
            }

            return prefab;
        }

        private static T GetOrAdd<T>(GameObject gameObject) where T : Component
        {
            T component = gameObject.GetComponent<T>();
            return component != null ? component : gameObject.AddComponent<T>();
        }

        private static void RemoveIfPresent<T>(GameObject gameObject)
            where T : Component
        {
            T component = gameObject.GetComponent<T>();
            if (component != null)
            {
                UnityEngine.Object.DestroyImmediate(component);
            }
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
