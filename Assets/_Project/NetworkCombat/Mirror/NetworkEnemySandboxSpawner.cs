using System;
using System.Collections;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>Development/load-test spawner for client-simulated Enemy snapshots.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    public sealed class NetworkEnemySandboxSpawner : NetworkBehaviour
    {
        [SerializeField] private GameObject enemyPrefab;
        [SerializeField, Min(0)] private int enemyCount = 120;
        [SerializeField, Min(1)] private int columns = 15;
        [SerializeField, Min(0.1f)] private float spacing = 1.5f;
        [SerializeField] private Vector2 origin = new Vector2(-10f, -6f);

        public void Configure(
            GameObject prefab,
            int count,
            int columnCount,
            float cellSpacing,
            Vector2 spawnOrigin)
        {
            enemyPrefab = prefab != null
                ? prefab
                : throw new ArgumentNullException(nameof(prefab));
            enemyCount = Mathf.Max(0, count);
            columns = Mathf.Max(1, columnCount);
            spacing = Mathf.Max(0.1f, cellSpacing);
            origin = spawnOrigin;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (enemyPrefab == null)
            {
                Debug.LogError("NetworkEnemySandboxSpawner requires an enemy prefab.", this);
                return;
            }

            StartCoroutine(SpawnWhenPlayerIsReady());
        }

        private IEnumerator SpawnWhenPlayerIsReady()
        {
            while (NetworkServer.active &&
                   (NetworkEnemySimulationWorld.Instance == null ||
                    !NetworkEnemySimulationWorld.Instance.HasEligiblePlayer))
            {
                yield return null;
            }

            if (!NetworkServer.active)
            {
                yield break;
            }

            for (int i = 0; i < enemyCount; i++)
            {
                int row = i / columns;
                int column = i % columns;
                Vector3 position = origin + new Vector2(column * spacing, row * spacing);
                GameObject enemy = Instantiate(enemyPrefab, position, Quaternion.identity);
                NetworkServer.Spawn(enemy);
            }
        }
    }
}
