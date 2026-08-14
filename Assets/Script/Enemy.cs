using System;
using System.Collections;
using Mirror;
using UnityEngine;

[RequireComponent(typeof(NetworkIdentity))]
[RequireComponent(typeof(Health))]
public sealed class Enemy : NetworkBehaviour
{
    [Header("内部组件")]
    [SerializeField]
    private Health health;

    [SerializeField]
    private EnemyChase chase;

    [SerializeField]
    private Collider2D bodyCollider;

    [Header("子弹瞄准点")]
    [SerializeField]
    private Transform hitPoint;

    [Header("死亡")]
    [SerializeField, Min(0f)]
    private float destroyDelay = 0.5f;
    
    [Header("伤害")]
    [SerializeField, Min(0f)]
    private int damage = 30;

    private bool _deathHandled;

    public bool IsDead =>
        health == null || health.IsDead;

    public Vector2 HitPosition =>
        hitPoint != null
            ? hitPoint.position
            : transform.position;

    private void OnTriggerEnter2D(Collider2D other)
    {
        Player player = other.GetComponent<Player>();
        player.ServerTakeDamage(damage);
    }

    private void Awake()
    {
        if (health == null)
            health = GetComponent<Health>();

        if (chase == null)
            chase = GetComponent<EnemyChase>();

        if (bodyCollider == null)
            bodyCollider = GetComponent<Collider2D>();

        if (health == null)
        {
            Debug.LogError(
                $"{name} 缺少 Health 组件。",
                this);
        }
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        health.ServerDied += ServerHandleDied;
    }

    public override void OnStopServer()
    {
        if (health != null)
            health.ServerDied -= ServerHandleDied;

        base.OnStopServer();
    }

    [Server]
    public void ServerTakeDamage(int damage)
    {
        if (health == null || health.IsDead)
            return;

        health.ServerTakeDamage(damage);
    }

    [Server]
    private void ServerHandleDied()
    {
        if (_deathHandled)
            return;

        _deathHandled = true;

        // 停止服务器上的游戏逻辑。
        if (chase != null)
            chase.enabled = false;

        if (bodyCollider != null)
            bodyCollider.enabled = false;

        // 通知客户端播放死亡表现。
        RpcPlayDeath();
        NetworkServer.Destroy(gameObject);
        // StartCoroutine(ServerDestroyAfterDelay());
    }

    [ClientRpc]
    private void RpcPlayDeath()
    {
        Debug.Log($"{name} 播放死亡表现。", this);

        // 在这里播放：
        // Animator 死亡动画
        // Spine 死亡动画
        // 粒子效果
        // 音效
        // Sprite 淡出
    }

    [Server]
    private IEnumerator ServerDestroyAfterDelay()
    {
        if (destroyDelay > 0f)
            yield return new WaitForSeconds(destroyDelay);

        NetworkServer.Destroy(gameObject);
    }
}