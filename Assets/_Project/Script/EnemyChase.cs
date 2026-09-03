using Mirror;
using UnityEngine;

public class EnemyChase : MonoBehaviour
{
    [Header("移动设置")]
    [SerializeField]
    private float moveSpeed = 2f;

    [SerializeField]
    private float stopDistance = 0.1f;
    
    private PlayerController target;
    
    public void Initialize(PlayerController newTarget)
    {
        target = newTarget;
    }
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    [Server]
    // Update is called once per frame
    void Update()
    {
        if (target == null)
        {
            return;
        }

        // 玩家位置 - 怪物位置 = 怪物指向玩家的向量
        Vector3 direction = target.transform.position - transform.position;

        // 这是纯2D游戏，不允许改变Z轴位置
        direction.z = 0f;

        // 已经接近玩家时停止，避免方向频繁抖动
        if (direction.sqrMagnitude <= stopDistance * stopDistance)
        {
            return;
        }

        direction.Normalize();

        transform.position +=
            direction * moveSpeed * Time.deltaTime;
    }
}
