using System;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Local
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class LocalEnemyChase : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float moveSpeed = 2f;
        [SerializeField, Min(0f)] private float stopDistance = 0.5f;

        private Rigidbody2D body;
        private Transform target;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
        }

        public void Initialize(Transform newTarget)
        {
            target = newTarget ?? throw new ArgumentNullException(nameof(newTarget));
        }

        private void FixedUpdate()
        {
            if (target == null)
            {
                return;
            }

            Vector2 offset = (Vector2)target.position - body.position;
            if (offset.sqrMagnitude <= stopDistance * stopDistance)
            {
                return;
            }

            body.MovePosition(body.position + offset.normalized * (moveSpeed * Time.fixedDeltaTime));
        }
    }
}
