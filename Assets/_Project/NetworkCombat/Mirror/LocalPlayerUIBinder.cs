using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.UI;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>The only HUD binding component that knows about Mirror.</summary>
    [DisallowMultipleComponent]
    public sealed class LocalPlayerUIBinder : MonoBehaviour
    {
        [SerializeField] private CombatHUDController combatHUD;

        private void OnEnable()
        {
            if (combatHUD != null)
            {
                combatHUD.Unbind();
            }
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
            if (combatHUD == null || !combatHUD.isActiveAndEnabled)
            {
                return;
            }

            NetworkIdentity player = NetworkClient.active
                ? NetworkClient.localPlayer
                : null;
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
            if (combatHUD != null)
            {
                combatHUD.Unbind();
            }
        }
    }
}
