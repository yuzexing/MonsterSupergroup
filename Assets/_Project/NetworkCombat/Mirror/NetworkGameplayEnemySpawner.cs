using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MonsterSupergroup.NetworkCombat
{
    [DisallowMultipleComponent]
    public sealed class NetworkGameplayEnemySpawner : MonoBehaviour
    {
        [SerializeField] private GameObject enemyPrefab;
        [SerializeField, Min(0.1f)] private float spawnDistance = 5f;

        private readonly HashSet<uint> spawnedForPlayers = new HashSet<uint>();
        private readonly List<NetworkEnemySimulationEndpoint> playerBuffer =
            new List<NetworkEnemySimulationEndpoint>(4);
        private NetworkEnemySimulationWorld world;
        private int runtimeMinimumSpawnHealth;

        public GameObject EnemyPrefab => enemyPrefab;

        public int SpawnedPlayerCount => spawnedForPlayers.Count;

        internal void ConfigureRuntimeMinimumSpawnHealth(int minimumHealth)
        {
            runtimeMinimumSpawnHealth = Mathf.Max(0, minimumHealth);
        }

        public void Configure(GameObject prefab, float distance)
        {
            enemyPrefab = prefab != null
                ? prefab
                : throw new System.ArgumentNullException(nameof(prefab));
            spawnDistance = Mathf.Max(0.1f, distance);
        }

        private IEnumerator Start()
        {
            if (!NetworkServer.active)
            {
                yield break;
            }

            while (NetworkServer.active &&
                   NetworkEnemySimulationWorld.Instance == null)
            {
                yield return null;
            }

            if (!NetworkServer.active)
            {
                yield break;
            }

            world = NetworkEnemySimulationWorld.Instance;
            world.ServerPlayerRegistered += HandlePlayerRegistered;
            world.ServerPlayerUnregistered += HandlePlayerUnregistered;
            world.GetEligiblePlayers(playerBuffer);
            for (int i = 0; i < playerBuffer.Count; i++)
            {
                SpawnForPlayer(playerBuffer[i]);
            }
        }

        private void OnDestroy()
        {
            if (world != null)
            {
                world.ServerPlayerRegistered -= HandlePlayerRegistered;
                world.ServerPlayerUnregistered -= HandlePlayerUnregistered;
            }
        }

        private void HandlePlayerRegistered(
            NetworkEnemySimulationEndpoint endpoint)
        {
            SpawnForPlayer(endpoint);
        }

        private void HandlePlayerUnregistered(
            NetworkEnemySimulationEndpoint endpoint)
        {
            // Existing Enemies remain in the shared world. The simulation world
            // reassigns or freezes them; a reconnect receives a new Player netId.
        }

        private void SpawnForPlayer(NetworkEnemySimulationEndpoint endpoint)
        {
            if (!NetworkServer.active || endpoint == null ||
                !endpoint.IsEligibleSimulationOwner || enemyPrefab == null ||
                !spawnedForPlayers.Add(endpoint.PlayerEntityId))
            {
                return;
            }

            Vector2 direction = DirectionFor(endpoint.PlayerEntityId);
            Vector3 position = endpoint.transform.position +
                (Vector3)(direction * spawnDistance);
            GameObject enemy = Instantiate(
                enemyPrefab,
                position,
                Quaternion.identity);
            NetworkEnemySimulationAgent agent =
                enemy.GetComponent<NetworkEnemySimulationAgent>();
            if (agent == null)
            {
                Debug.LogError(
                    "Gameplay Enemy prefab requires NetworkEnemySimulationAgent.",
                    enemy);
                Destroy(enemy);
                return;
            }

            agent.ConfigureRuntimeMinimumHealthOverride(
                runtimeMinimumSpawnHealth);
            agent.ConfigureInitialServerTarget(endpoint.PlayerEntityId);
            SceneManager.MoveGameObjectToScene(enemy, gameObject.scene);
            NetworkServer.Spawn(enemy);
        }

        private static Vector2 DirectionFor(uint playerEntityId)
        {
            switch (playerEntityId % 4u)
            {
            case 0u:
                return Vector2.right;
            case 1u:
                return Vector2.up;
            case 2u:
                return Vector2.left;
            default:
                return Vector2.down;
            }
        }
    }
}
