using MonsterSupergroup.Gameplay.Local;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    public sealed class NetworkPlayerBootstrap : NetworkBehaviour
    {
        [SerializeField] private PlayerLoader playerLoader;
        [SerializeField] private LocalPlayerMovement movement;

        private void Awake()
        {
            if (playerLoader == null)
            {
                playerLoader = GetComponent<PlayerLoader>();
            }

            if (movement == null)
            {
                movement = GetComponent<LocalPlayerMovement>();
            }
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (movement != null)
            {
                movement.enabled = isOwned;
            }
        }

        public override void OnStartAuthority()
        {
            base.OnStartAuthority();
            if (movement != null)
            {
                movement.enabled = true;
            }

            if (playerLoader == null)
            {
                Debug.LogError("NetworkPlayerBootstrap requires PlayerLoader.", this);
                return;
            }

            playerLoader.Load(transform.position);
        }

        public override void OnStopAuthority()
        {
            playerLoader?.Unload();
            if (movement != null)
            {
                movement.enabled = false;
            }

            base.OnStopAuthority();
        }
    }
}
