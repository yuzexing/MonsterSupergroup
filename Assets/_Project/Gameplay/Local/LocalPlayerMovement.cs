using UnityEngine;
using UnityEngine.InputSystem;

namespace MonsterSupergroup.Gameplay.Local
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class LocalPlayerMovement : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float moveSpeed = 5f;

        private Rigidbody2D body;
        private Vector2 input;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                input = Vector2.zero;
                return;
            }

            input = new Vector2(
                (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
                (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f)).normalized;
        }

        private void FixedUpdate()
        {
            body.MovePosition(body.position + input * (moveSpeed * Time.fixedDeltaTime));
        }
    }
}
