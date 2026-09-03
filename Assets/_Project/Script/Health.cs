using System;
using Mirror;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class Health : NetworkBehaviour
{
    [Header("生命值")]
    [SerializeField, Min(1)]
    private int maxHealth = 100;

    [SyncVar(hook = nameof(OnCurrentHealthChanged))]
    private int currentHealth;

    [SyncVar(hook = nameof(OnDeadStateChanged))]
    private bool isDead;

    public int MaxHealth => maxHealth;
    public int CurrentHealth => currentHealth;
    public bool IsDead => isDead;

    /// <summary>
    /// 客户端表现事件：血量发生变化。
    /// </summary>
    public event Action<int, int> HealthChanged;

    /// <summary>
    /// 客户端表现事件：死亡状态发生变化。
    /// </summary>
    public event Action<bool, bool> DeadStateChanged;

    /// <summary>
    /// 服务器逻辑事件：单位死亡。
    /// </summary>
    public event Action ServerDied;

    public override void OnStartServer()
    {
        base.OnStartServer();

        currentHealth = maxHealth;
        isDead = false;
    }

    [Server]
    public void ServerTakeDamage(int damage)
    {
        if (isDead || damage <= 0)
            return;

        currentHealth = Mathf.Max(
            0,
            currentHealth - damage);

        if (currentHealth > 0)
            return;

        isDead = true;

        // 服务器立即处理权威死亡逻辑。
        ServerDied?.Invoke();
    }

    [Server]
    public void ServerHeal(int amount)
    {
        if (isDead || amount <= 0)
            return;

        currentHealth = Mathf.Min(
            maxHealth,
            currentHealth + amount);
    }

    [Server]
    public void ServerResetHealth()
    {
        currentHealth = maxHealth;
        isDead = false;
    }

    private void OnCurrentHealthChanged(
        int oldHealth,
        int newHealth)
    {
        // SyncVar Hook 主要用于客户端表现。
        HealthChanged?.Invoke(oldHealth, newHealth);
    }

    private void OnDeadStateChanged(
        bool oldIsDead,
        bool newIsDead)
    {
        DeadStateChanged?.Invoke(
            oldIsDead,
            newIsDead);
    }
}