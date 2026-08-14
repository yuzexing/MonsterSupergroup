using Mirror;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class Projectile : NetworkBehaviour
{
    [Header("移动")]
    [SerializeField, Min(0.1f)]
    private float moveSpeed = 10f;

    [SerializeField, Min(0.1f)]
    private float maxLifetime = 5f;

    [Header("旋转")]
    [SerializeField]
    private bool rotateTowardsTarget = true;

    [Tooltip("精灵默认朝右填 0；默认朝上填 -90。")]
    [SerializeField]
    private float rotationOffset;

    private Rigidbody2D _rigidbody;

    private Enemy _target;
    private int _damage;

    private float _remainingLifetime;
    private bool _initialized;
    private bool _hasHit;

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody2D>();

        _rigidbody.gravityScale = 0f;
        _rigidbody.bodyType = RigidbodyType2D.Kinematic;
        _rigidbody.collisionDetectionMode =
            CollisionDetectionMode2D.Continuous;

        Collider2D projectileCollider =
            GetComponent<Collider2D>();

        projectileCollider.isTrigger = true;
    }

    /// <summary>
    /// 子弹生成后调用，设置目标和伤害。
    /// </summary>
    public void Initialize(Enemy target, int damage)
    {
        if (target == null)
        {
            Debug.LogError(
                "Projectile 初始化失败：目标 Enemy 为空。",
                this);

            Destroy(gameObject);
            return;
        }

        _target = target;
        _damage = Mathf.Max(0, damage);
        _remainingLifetime = maxLifetime;
        _initialized = true;
    }

    private void FixedUpdate()
    {
        if (!NetworkServer.active)
            return;
        
        if (!_initialized || _hasHit)
            return;

        _remainingLifetime -= Time.fixedDeltaTime;

        if (_remainingLifetime <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        // if (_target == null || _target.IsDead)
        // {
        //     Destroy(gameObject);
        //     return;
        // }

        Vector2 currentPosition = _rigidbody.position;
        Vector2 targetPosition = _target.HitPosition;

        Vector2 offset =
            targetPosition - currentPosition;

        float moveDistance =
            moveSpeed * Time.fixedDeltaTime;

        // 当前帧可以直接到达目标。
        // if (offset.sqrMagnitude <=
        //     moveDistance * moveDistance)
        // {
        //     _rigidbody.MovePosition(targetPosition);
        //     Hit(_target);
        //     return;
        // }

        Vector2 direction = offset.normalized;

        Vector2 nextPosition =
            currentPosition +
            direction * moveDistance;

        _rigidbody.MovePosition(nextPosition);

        if (rotateTowardsTarget)
        {
            float angle =
                Mathf.Atan2(direction.y, direction.x) *
                Mathf.Rad2Deg;

            _rigidbody.MoveRotation(
                angle + rotationOffset);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!NetworkServer.active)
            return;
        
        if (!_initialized || _hasHit)
            return;

        // 敌人的碰撞体可能位于子节点。
        Enemy enemy =
            other.GetComponentInParent<Enemy>();

        if (enemy == null)
            return;

        Hit(enemy);
    }

    private void Hit(Enemy enemy)
    {
        if (_hasHit || enemy == null)
            return;

        // 只有服务器有权产生真实伤害。
        if (!NetworkServer.active)
            return;

        _hasHit = true;

        enemy.ServerTakeDamage(_damage);

        // 子弹本身也是网络对象时：
        NetworkServer.Destroy(gameObject);
    }
}