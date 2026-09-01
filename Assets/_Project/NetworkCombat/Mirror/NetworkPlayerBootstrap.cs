using AstralShift.HellMaiden;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Controllers;
using AstralShift.HellMaiden.Items;
using AstralShift.HellMaiden.Player;
using AstralShift.Managers;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    public sealed class NetworkPlayerBootstrap : NetworkBehaviour
    {
        [SerializeField] private PlayerBuildRuntime playerBuildRuntime;
        [SerializeField] private RuntimeDB runtimeDatabase;
        [SerializeField] private PlayerMovement playerMovement;
        [SerializeField] private PlayerCombatantBinding combatantBinding;

        private PlayerController_HMD ownerPlayerController;

        private void Awake()
        {
            if (playerBuildRuntime == null)
            {
                playerBuildRuntime = GetComponent<PlayerBuildRuntime>();
            }

            if (playerMovement == null)
            {
                playerMovement = GetComponent<PlayerMovement>();
            }

            if (combatantBinding == null)
            {
                combatantBinding = GetComponent<PlayerCombatantBinding>();
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
            if (playerMovement != null)
            {
                playerMovement.enabled = isOwned;
            }

            combatantBinding?.SetLocalMutationAuthority(isOwned);
        }

        public override void OnStartAuthority()
        {
            base.OnStartAuthority();
            if (playerMovement != null)
            {
                playerMovement.enabled = true;
            }

            combatantBinding?.SetLocalMutationAuthority(true);
            combatantBinding?.Combatant?.ResetCombatant();
            EnsureLocalPlayerRegistration();
            ActivateOwnerPlayerController();

            if (playerBuildRuntime == null)
            {
                Debug.LogError(
                    "NetworkPlayerBootstrap requires PlayerBuildRuntime.",
                    this);
                return;
            }

            RuntimeDB database = ResolveSharedRuntimeDatabase();
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
            ReleaseOwnerPlayerController();
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

        public RuntimeDB ResolveSharedRuntimeDatabase()
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

        private void ActivateOwnerPlayerController()
        {
            if (ownerPlayerController != null)
            {
                return;
            }

            ControllerManager manager = ControllerManager.Instance;
            if (manager == null || manager.Stack == null)
            {
                Debug.LogError(
                    "NetworkPlayerBootstrap cannot activate PlayerController_HMD " +
                    "before ControllerManager is initialized.",
                    this);
                return;
            }

            ownerPlayerController =
                manager.OverrideGameController<PlayerController_HMD>();
            if (ownerPlayerController == null)
            {
                Debug.LogError(
                    "NetworkPlayerBootstrap requires a subscribed " +
                    "PlayerController_HMD for the Owner Player.",
                    this);
            }
        }

        private void ReleaseOwnerPlayerController()
        {
            if (ownerPlayerController == null)
            {
                return;
            }

            ControllerManager.Instance?.ReleaseGameController(ownerPlayerController);
            ownerPlayerController = null;
        }
    }
}
