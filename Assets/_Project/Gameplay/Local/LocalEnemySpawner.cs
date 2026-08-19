using System;
using System.Collections.Generic;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Local
{
    [DisallowMultipleComponent]
    public sealed class LocalEnemySpawner : MonoBehaviour
    {
        [SerializeField] private CombatTeamBehaviour enemyPrefab;
        [SerializeField] private Transform enemyContainer;
        [SerializeField] private SpriteRenderer groundRenderer;
        [SerializeField, Min(0.01f)] private float spawnInterval = 1f;
        [SerializeField, Min(1)] private int maxEnemyCount = 50;
        [SerializeField, Min(0f)] private float spawnInset = 0.5f;

        private readonly List<CombatTeamBehaviour> enemies = new List<CombatTeamBehaviour>();
        private Transform player;
        private float spawnTimer;
        private bool initialized;

        public void Configure(
            CombatTeamBehaviour newEnemyPrefab,
            Transform newEnemyContainer,
            SpriteRenderer newGroundRenderer)
        {
            enemyPrefab = newEnemyPrefab ?? throw new ArgumentNullException(nameof(newEnemyPrefab));
            enemyContainer = newEnemyContainer ?? throw new ArgumentNullException(nameof(newEnemyContainer));
            groundRenderer = newGroundRenderer ?? throw new ArgumentNullException(nameof(newGroundRenderer));
        }

        public void Initialize(Transform playerTransform)
        {
            if (enemyPrefab == null || enemyContainer == null || groundRenderer == null)
            {
                throw new InvalidOperationException(
                    "LocalEnemySpawner requires enemy prefab, container, and ground renderer references.");
            }

            player = playerTransform ?? throw new ArgumentNullException(nameof(playerTransform));
            spawnTimer = spawnInterval;
            initialized = true;
        }

        private void Update()
        {
            if (!initialized)
            {
                return;
            }

            spawnTimer += Time.deltaTime;
            if (spawnTimer < spawnInterval)
            {
                return;
            }

            enemies.RemoveAll(enemy => enemy == null);
            if (enemies.Count >= maxEnemyCount)
            {
                return;
            }

            spawnTimer -= spawnInterval;
            SpawnEnemy();
        }

        private void SpawnEnemy()
        {
            CombatTeamBehaviour enemy = Instantiate(
                enemyPrefab,
                GetRandomEdgePosition(),
                Quaternion.identity,
                enemyContainer);
            LocalEnemyChase chase = enemy.GetComponent<LocalEnemyChase>();
            if (chase == null)
            {
                Destroy(enemy.gameObject);
                throw new InvalidOperationException("The local enemy prefab is missing LocalEnemyChase.");
            }

            chase.Initialize(player);
            enemies.Add(enemy);
        }

        private Vector3 GetRandomEdgePosition()
        {
            Bounds bounds = groundRenderer.bounds;
            float left = bounds.min.x + spawnInset;
            float right = bounds.max.x - spawnInset;
            float bottom = bounds.min.y + spawnInset;
            float top = bounds.max.y - spawnInset;
            int edge = UnityEngine.Random.Range(0, 4);

            return edge switch
            {
                0 => new Vector3(UnityEngine.Random.Range(left, right), top, -1f),
                1 => new Vector3(UnityEngine.Random.Range(left, right), bottom, -1f),
                2 => new Vector3(left, UnityEngine.Random.Range(bottom, top), -1f),
                _ => new Vector3(right, UnityEngine.Random.Range(bottom, top), -1f)
            };
        }
    }
}
