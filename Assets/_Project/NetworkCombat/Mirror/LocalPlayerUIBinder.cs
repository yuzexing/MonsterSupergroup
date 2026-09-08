using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.UI;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>Resolves local player ownership for HUD and menu presentation.</summary>
    [DisallowMultipleComponent]
    public sealed class LocalPlayerUIBinder : MonoBehaviour
    {
        [SerializeField] private CombatHUDController combatHUD;
        [SerializeField] private CardPickMenu cardPickMenu;

        private void OnEnable()
        {
            if (combatHUD != null)
            {
                combatHUD.Unbind();
            }
            if (cardPickMenu != null) cardPickMenu.Unbind();
            // Bind in LateUpdate, after all child presentation components have
            // finished Awake/Start. StatusBar.Awake initializes its images.
        }

        private void LateUpdate()
        {
            // Check identity, not health. This also handles UI loading after the
            // player, replacement, authority loss and reconnect without a registry.
            RefreshBinding();
        }

        private void RefreshBinding()
        {
            NetworkIdentity player = NetworkClient.active
                && !GameplayRuntimeEnvironment.IsDedicatedServer
                ? NetworkClient.localPlayer
                : null;
            if (player != null && (!player.isOwned || !player.gameObject.activeInHierarchy))
                player = null;
            if (cardPickMenu != null && cardPickMenu.isActiveAndEnabled)
            {
                ModifierSelectionController selection = player != null
                    ? player.GetComponent<ModifierSelectionController>() : null;
                if (selection != null && !selection.isActiveAndEnabled) selection = null;
                cardPickMenu.Bind(selection);
            }

            if (combatHUD == null || !combatHUD.isActiveAndEnabled) return;
            CombatantBehaviour combatant = player != null && player.isOwned &&
                player.gameObject.activeInHierarchy
                ? player.GetComponent<CombatantBehaviour>()
                : null;
            if (combatant != null && !combatant.isActiveAndEnabled)
            {
                combatant = null;
            }

            if (ReferenceEquals(combatant, combatHUD.BoundCombatant))
            {
                return;
            }

            combatHUD.Bind(combatant);
        }

        private void OnDisable()
        {
            if (cardPickMenu != null) cardPickMenu.Unbind();
            if (combatHUD != null)
            {
                combatHUD.Unbind();
            }
        }
    }
}
