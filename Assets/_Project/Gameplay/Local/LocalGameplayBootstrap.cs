using System;
using AstralShift.HellMaiden;
using AstralShift.HellMaiden.Player;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Local
{
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public sealed class LocalGameplayBootstrap : MonoBehaviour
    {
        [SerializeField] private PlayerLoader playerPrefab;
        [SerializeField] private Transform playerSpawn;
        [SerializeField] private LocalEnemySpawner enemySpawner;

        private PlayerLoader playerInstance;

        public PlayerLoader PlayerInstance => playerInstance;

        public void Configure(
            PlayerLoader newPlayerPrefab,
            Transform newPlayerSpawn,
            LocalEnemySpawner newEnemySpawner)
        {
            playerPrefab = newPlayerPrefab ?? throw new ArgumentNullException(nameof(newPlayerPrefab));
            playerSpawn = newPlayerSpawn ?? throw new ArgumentNullException(nameof(newPlayerSpawn));
            enemySpawner = newEnemySpawner ?? throw new ArgumentNullException(nameof(newEnemySpawner));
        }

        private void Start()
        {
            if (playerPrefab == null || playerSpawn == null || enemySpawner == null)
            {
                enabled = false;
                throw new InvalidOperationException(
                    "LocalGameplayBootstrap requires player prefab, spawn point, and enemy spawner references.");
            }

            playerInstance = Instantiate(playerPrefab, playerSpawn.position, Quaternion.identity);
            playerInstance.Load(playerSpawn.position);
            enemySpawner.Initialize(playerInstance.transform);
            GameDirector.Instance.SetPlayer(playerInstance.GetComponent<PlayerMovement>());
        }

        private void OnDestroy()
        {
            if (playerInstance != null)
            {
                playerInstance.Unload();
            }
        }
    }
}
