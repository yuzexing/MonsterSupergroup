using UnityEngine;
using UnityEngine.InputSystem;

public class CoinSpawner : MonoBehaviour
{
    [Header("引用")]
    [SerializeField]
    private GameObject coinPrefab;

    [SerializeField]
    private Camera mainCamera;

    [SerializeField]
    private Transform coinContainer;

    [Header("生成设置")]
    [SerializeField]
    private float spawnInterval = 0.5f;

    [Tooltip("金币所在的世界坐标 Z")]
    [SerializeField]
    private float coinZ = 0f;

    private float nextSpawnTime;

    private void Awake()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }
    }

    private void Update()
    {
        Mouse mouse = Mouse.current;

        if (mouse == null || coinPrefab == null || mainCamera == null)
        {
            return;
        }

        // 刚按下左键：立即生成一次
        if (mouse.leftButton.wasPressedThisFrame)
        {
            SpawnCoinAtMousePosition();

            nextSpawnTime = Time.time + spawnInterval;
        }

        // 持续按住左键：每隔 spawnInterval 生成一次
        if (mouse.leftButton.isPressed &&
            Time.time >= nextSpawnTime)
        {
            SpawnCoinAtMousePosition();

            nextSpawnTime += spawnInterval;
        }
    }

    private void SpawnCoinAtMousePosition()
    {
        Vector2 mouseScreenPosition =
            Mouse.current.position.ReadValue();

        Vector3 screenPosition = new Vector3(
            mouseScreenPosition.x,
            mouseScreenPosition.y,
            coinZ - mainCamera.transform.position.z
        );

        Vector3 worldPosition =
            mainCamera.ScreenToWorldPoint(screenPosition);

        worldPosition.z = coinZ;

        Instantiate(
            coinPrefab,
            worldPosition,
            Quaternion.identity,
            coinContainer
        );
    }
}