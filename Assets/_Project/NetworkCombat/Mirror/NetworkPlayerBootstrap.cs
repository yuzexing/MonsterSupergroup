using AstralShift.HellMaiden;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Items;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.Local;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    public sealed class NetworkPlayerBootstrap : NetworkBehaviour
    {
        [SerializeField] private PlayerBuildRuntime playerBuildRuntime;
        [SerializeField] private RuntimeDB runtimeDatabase;
        [SerializeField] private LocalPlayerMovement movement;
        [SerializeField] private PlayerMovement playerMovement;
        [SerializeField] private PlayerCombatantBinding combatantBinding;

        private void Awake()
        {
            if (playerBuildRuntime == null)
            {
                playerBuildRuntime = GetComponent<PlayerBuildRuntime>();
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
            combatantBinding?.Combatant?.ResetCombatant();
            EnsureLocalPlayerRegistration();

            if (playerBuildRuntime == null)
            {
                Debug.LogError(
                    "NetworkPlayerBootstrap requires PlayerBuildRuntime.",
                    this);
                return;
            }

            RuntimeDB database = ResolveRuntimeDatabase();
            if (database == null)
            {
                Debug.LogError(
                    "NetworkPlayerBootstrap could not find the shared RuntimeDB.",
                    this);
                return;
            }

            try
            {
                playerBuildRuntime.StartInitialBuild(database);
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        public override void OnStopAuthority()
        {
            playerBuildRuntime?.ClearBuild();
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

        private RuntimeDB ResolveRuntimeDatabase()
        {
            if (runtimeDatabase != null)
            {
                return runtimeDatabase;
            }

            if (GameDirector.Instance != null &&
                GameDirector.Instance.runtimeDB != null)
            {
                runtimeDatabase = GameDirector.Instance.runtimeDB;
                return runtimeDatabase;
            }

            runtimeDatabase = FindFirstObjectByType<RuntimeDB>();
            return runtimeDatabase;
        }
    }
}
