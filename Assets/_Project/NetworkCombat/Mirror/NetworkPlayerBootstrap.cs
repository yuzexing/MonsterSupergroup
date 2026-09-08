using AstralShift.HellMaiden;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Items;
using AstralShift.HellMaiden.Player;
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

        private readonly LocalPlayerInputBinding ownerInput = new LocalPlayerInputBinding();
        private ModifierSelectionController modifierSelection;

        public bool IsLocalOwnerBound => ownerInput.BoundPlayer != null;

        private void Awake()
        {
            modifierSelection = GetComponent<ModifierSelectionController>();
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
                playerMovement.ConfigureNetworkLifecycle();
                playerMovement.enabled = false;
            }

            combatantBinding?.SetLocalMutationAuthority(false);
        }

        public void EnsurePlayerRuntimeInitialized()
        {
            if (playerMovement == null) playerMovement = GetComponent<PlayerMovement>();
            if (playerMovement == null)
                throw new System.InvalidOperationException("NetworkPlayerBootstrap requires PlayerMovement.");
            playerMovement.ConfigureNetworkLifecycle();
            playerMovement.EnsureRuntimeInitialized();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            EnsurePlayerRuntimeInitialized();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            EnsurePlayerRuntimeInitialized();
            if (playerMovement != null)
            {
                playerMovement.enabled = isOwned && !GameplayRuntimeEnvironment.IsDedicatedServer;
            }

            combatantBinding?.SetLocalMutationAuthority(isOwned && !GameplayRuntimeEnvironment.IsDedicatedServer);
        }

        public override void OnStartAuthority()
        {
            base.OnStartAuthority();
            EnsurePlayerRuntimeInitialized();
            if (GameplayRuntimeEnvironment.IsDedicatedServer) return;
            if (playerMovement != null)
            {
                playerMovement.enabled = true;
            }

            combatantBinding?.SetLocalMutationAuthority(true);
            EnsureLocalPlayerRegistration();
            ownerInput.Bind(playerMovement);

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
                // The server already owns the host's canonical Build. A remote Owner
                // creates the execution replica, populated by NetworkModifierSelection.
                if (!playerBuildRuntime.IsBuildActive)
                    playerBuildRuntime.StartInitialBuild(database);
                playerBuildRuntime.SetWeaponExecutionEnabled(true);
                modifierSelection?.Bind(playerBuildRuntime);
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        public override void OnStopAuthority()
        {
            ReleaseLocalBuild();
            base.OnStopAuthority();
        }

        public override void OnStopClient()
        {
            // Mirror's client shutdown does not call OnStopAuthority first.
            ReleaseLocalBuild();
            base.OnStopClient();
        }

        private void ReleaseLocalBuild()
        {
            modifierSelection?.Unbind();
            ownerInput.Dispose();
            if (!isServer) playerBuildRuntime?.ClearBuild();
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

        }

        [ClientCallback]
        private void Update()
        {
            if (isOwned && !GameplayRuntimeEnvironment.IsDedicatedServer)
            {
                EnsureLocalPlayerRegistration();
                ownerInput.Refresh();
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

            runtimeDatabase = FindFirstObjectByType<RuntimeDB>();
            return runtimeDatabase;
        }

        private void OnDestroy() => ownerInput.Dispose();
    }
}
