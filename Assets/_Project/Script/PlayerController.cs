using System;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : NetworkBehaviour
{
    private float moveSpeed = 5f;
    
    private Vector2 moveInput;

    // Update is called once per frame
    void Update()
    {
        if (!isLocalPlayer)
        {
            return;
        }
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        float x =
            (keyboard.dKey.isPressed ? 1f : 0f) -
            (keyboard.aKey.isPressed ? 1f : 0f);

        float y =
            (keyboard.wKey.isPressed ? 1f : 0f) -
            (keyboard.sKey.isPressed ? 1f : 0f);

        moveInput = new Vector2(x, y).normalized;

        moveInput = moveInput.normalized;
    }
    private void FixedUpdate()
    {
        Vector3 movement = new Vector3(moveInput.x, moveInput.y, 0);
        transform.position =
            transform.position + movement * moveSpeed * Time.fixedDeltaTime;
        
    }
}
