using System.Collections.Generic;
using Mirror;
using UnityEngine;

public class EnemySpawner : NetworkBehaviour
{
    [Header("引用")]
    [SerializeField]
    private EnemyChase enemyPrefab;

    [SerializeField]
    private PlayerController player;

    [SerializeField]
    private Transform enemyContainer;

    [SerializeField]
    private SpriteRenderer groundRenderer;

    [Header("生成设置")]
    [SerializeField]
    private float spawnInterval = 1f;

    [SerializeField]
    private int maxEnemyCount = 50;

    [Tooltip("生成点距离地面边缘的距离。正数表示向内部缩进。")]
    [SerializeField]
    private float spawnInset = 0.5f;

    private float spawnTimer;

    private readonly List<EnemyChase> enemies = new();

    private void Awake()
    {
        if (groundRenderer == null)
        {
            Debug.LogError("EnemySpawner 没有指定 Ground 的 SpriteRenderer。");
        }
    }

    private void Update()
    {
        if (!player)
        {
            player = FindFirstObjectByType<PlayerController>();
        }
        if (groundRenderer == null)
        {
            return;
        }

        spawnTimer += Time.deltaTime;

        if (spawnTimer < spawnInterval)
        {
            return;
        }

        spawnTimer -= spawnInterval;

        enemies.RemoveAll(enemy => enemy == null);

        if (enemies.Count >= maxEnemyCount)
        {
            return;
        }

        SpawnEnemy();
    }

    [Server]
    private void SpawnEnemy()
    {
        Vector3 spawnPosition = GetRandomEdgePosition();

        EnemyChase enemy = Instantiate(
            enemyPrefab,
            spawnPosition,
            Quaternion.identity,
            enemyContainer
        );

        enemy.Initialize(player);
        enemies.Add(enemy);
        NetworkServer.Spawn(enemy.gameObject);
    }

    private Vector3 GetRandomEdgePosition()
    {
        // SpriteRenderer.bounds 是世界坐标范围
        Bounds bounds = groundRenderer.bounds;

        float left = bounds.min.x + spawnInset;
        float right = bounds.max.x - spawnInset;
        float bottom = bounds.min.y + spawnInset;
        float top = bounds.max.y - spawnInset;

        int edge = Random.Range(0, 4);

        return edge switch
        {
            // 上边
            0 => new Vector3(
                Random.Range(left, right),
                top,
                groundRenderer.transform.position.z
            ),

            // 下边
            1 => new Vector3(
                Random.Range(left, right),
                bottom,
                groundRenderer.transform.position.z
            ),

            // 左边
            2 => new Vector3(
                left,
                Random.Range(bottom, top),
                groundRenderer.transform.position.z
            ),

            // 右边
            _ => new Vector3(
                right,
                Random.Range(bottom, top),
                groundRenderer.transform.position.z
            )
        };
    }
}