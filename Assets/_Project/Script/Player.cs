using Mirror;
using UnityEngine;

[RequireComponent(typeof(NetworkIdentity))]
[RequireComponent(typeof(Health))]
public sealed class Player : NetworkBehaviour
{
    [Header("内部组件")]
    [SerializeField]
    private Health health;

    [Header("死亡时关闭的本地组件")]
    [SerializeField]
    private Behaviour movement;

    [SerializeField]
    private Behaviour shooter;

    public bool IsDead =>
        health == null || health.IsDead;

    public int CurrentHealth =>
        health != null ? health.CurrentHealth : 0;

    private void Awake()
    {
        if (health == null)
            health = GetComponent<Health>();

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

        // 服务器监听权威死亡事件。
        health.ServerDied += ServerHandleDied;
    }

    public override void OnStopServer()
    {
        if (health != null)
            health.ServerDied -= ServerHandleDied;

        base.OnStopServer();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // 客户端监听同步后的表现事件。
        health.HealthChanged += HandleHealthChanged;
        health.DeadStateChanged += HandleDeadStateChanged;

        // SyncVar 初始值在 OnStartClient 前已经同步，
        // 所以这里手动初始化一次 UI 和状态。
        HandleHealthChanged(
            health.CurrentHealth,
            health.CurrentHealth);

        HandleDeadStateChanged(
            health.IsDead,
            health.IsDead);
    }

    public override void OnStopClient()
    {
        if (health != null)
        {
            health.HealthChanged -= HandleHealthChanged;
            health.DeadStateChanged -= HandleDeadStateChanged;
        }

        base.OnStopClient();
    }

    [Server]
    public void ServerTakeDamage(int damage)
    {
        if (health == null || health.IsDead)
            return;

        health.ServerTakeDamage(damage);
    }

    [Server]
    public void ServerHeal(int amount)
    {
        if (health == null)
            return;

        health.ServerHeal(amount);
    }

    [Server]
    public void ServerRespawn(Vector2 position)
    {
        transform.position = position;

        health.ServerResetHealth();

        // 服务器上的其他复活逻辑：
        // 重置技能、Buff、无敌时间等。
    }

    [Server]
    private void ServerHandleDied()
    {
        Debug.Log(
            $"{name} 在服务器上判定死亡。",
            this);

        // 这里执行服务器权威逻辑：
        // 1. 停止攻击和移动请求
        // 2. 清除部分 Buff
        // 3. 通知复活系统
        // 4. 判断是否全队死亡
        //
        // 通常不要直接销毁 Player 网络对象。
    }

    private void HandleHealthChanged(
        int oldHealth,
        int newHealth)
    {
        // 这里只更新自己的 HUD。
        if (!isLocalPlayer)
            return;

        Debug.Log(
            $"本地玩家生命值：{oldHealth} → {newHealth}",
            this);

        // healthBar.SetValue(newHealth, health.MaxHealth);
    }

    private void HandleDeadStateChanged(
        bool oldIsDead,
        bool newIsDead)
    {
        // 每个客户端只控制自己的输入脚本。
        if (!isLocalPlayer)
            return;

        if (movement != null)
            movement.enabled = !newIsDead;

        if (shooter != null)
            shooter.enabled = !newIsDead;

        if (newIsDead)
        {
            Debug.Log("本地玩家死亡。", this);

            // 显示复活倒计时或结算 UI。
        }
        else
        {
            Debug.Log("本地玩家复活。", this);
        }
    }
}