using UnityEngine;
using Mirror;

public class PlayerShooter : NetworkBehaviour
{
    [Header("引用")]
    [SerializeField]
    private Projectile projectilePrefab;

    [SerializeField]
    private Transform firePoint;

    [Header("攻击")]
    [SerializeField, Min(0.01f)]
    private float attackInterval = 2f;
    
    [Header("攻击")]
    [SerializeField, Min(0)]
    private float _nextAttackTime;
    
    [Header("攻击")]
    [SerializeField, Min(0)]
    private int damage = 10;
    private void Start()
    {
        // 游戏开始后立即允许攻击。
        _nextAttackTime = Time.time;
    }
    
    [Server]
    private void Update()
    {
        
        // 还没有到达下一次攻击时间。
        if (Time.time < _nextAttackTime)
            return;

        // 记录下一次允许攻击的时间。
        _nextAttackTime = Time.time + attackInterval;

        // 只在需要攻击时搜索目标，而不是每帧搜索。
        Enemy target = FindAnyObjectByType<Enemy>();

        if (target == null || target.IsDead)
            return;

        Fire(target);
    }

    public void Fire(Enemy target)
    {
        if (projectilePrefab == null)
        {
            Debug.LogError(
                "没有配置 Projectile Prefab。",
                this);
            return;
        }

        if (target == null || target.IsDead)
            return;

        Vector3 spawnPosition =
            firePoint != null
                ? firePoint.position
                : transform.position;

        Projectile projectile = Instantiate(
            projectilePrefab,
            spawnPosition,
            Quaternion.identity);

        projectile.Initialize(target, damage);
        NetworkServer.Spawn(projectile.gameObject);
    }
}