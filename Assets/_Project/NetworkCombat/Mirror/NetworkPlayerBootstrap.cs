using AstralShift.HellMaiden;
using AstralShift.HellMaiden.Items;
using AstralShift.HellMaiden.Player;
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
        [SerializeField] private PlayerMovement playerMovement;
        [SerializeField] private PlayerCombatantBinding combatantBinding;

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

            if (playerMovement == null)
            {
                playerMovement = GetComponent<PlayerMovement>();
            }

            if (combatantBinding == null)
            {
                combatantBinding = GetComponent<PlayerCombatantBinding>();
            }

            if (movement != null)
            {
                movement.enabled = false;
            }

            if (playerMovement != null)
            {
                playerMovement.enabled = false;
            }

            combatantBinding?.SetLocalMutationAuthority(false);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (movement != null)
            {
                movement.enabled = isOwned;
            }

            if (playerMovement != null)
            {
                playerMovement.enabled = isOwned;
            }

            combatantBinding?.SetLocalMutationAuthority(isOwned);
        }

        public override void OnStartAuthority()
        {
            base.OnStartAuthority();
            if (movement != null)
            {
                movement.enabled = true;
            }

            if (playerMovement != null)
            {
                playerMovement.enabled = true;
            }

            combatantBinding?.SetLocalMutationAuthority(true);
            EnsureLocalPlayerRegistration();

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
            if (LootManager.Instance != null && playerMovement != null)
            {
                LootManager.Instance.UnRegisterLootCollector(playerMovement);
            }
            if (GameDirector.Instance != null &&
                GameDirector.Instance.Player == playerMovement)
            {
                GameDirector.Instance.SetPlayer(null);
            }

            combatantBinding?.SetLocalMutationAuthority(false);
            if (movement != null)
            {
                movement.enabled = false;
            }

            if (playerMovement != null)
            {
                playerMovement.enabled = false;
            }

            base.OnStopAuthority();
        }

        [ClientCallback]
        private void Update()
        {
            if (isOwned)
            {
                EnsureLocalPlayerRegistration();
            }
        }

        private void EnsureLocalPlayerRegistration()
        {
            if (playerMovement == null)
            {
                return;
            }

            if (GameDirector.Instance != null &&
                GameDirector.Instance.Player != playerMovement)
            {
                GameDirector.Instance.SetPlayer(playerMovement);
            }

            LootManager.Instance?.RegisterLootCollector(playerMovement);
        }
    }
}
