using System;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Local
{
    [DisallowMultipleComponent]
    public sealed class PlayerLoader : MonoBehaviour
    {
        [SerializeField] private PlayerHandBehaviour playerHand;
        [SerializeField] private CombatantBehaviour combatant;
        [SerializeField] private WeaponDefinition initialWeapon;

        public PlayerHandBehaviour PlayerHand => playerHand;
        public CombatantBehaviour Combatant => combatant;
        public bool IsLoaded { get; private set; }

        public void Configure(
            PlayerHandBehaviour hand,
            CombatantBehaviour playerCombatant,
            WeaponDefinition weapon)
        {
            playerHand = hand ?? throw new ArgumentNullException(nameof(hand));
            combatant = playerCombatant ?? throw new ArgumentNullException(nameof(playerCombatant));
            initialWeapon = weapon ?? throw new ArgumentNullException(nameof(weapon));
        }

        public void Load(Vector3 spawnPosition)
        {
            if (playerHand == null || combatant == null || initialWeapon == null)
            {
                throw new InvalidOperationException(
                    "PlayerLoader requires PlayerHandBehaviour, CombatantBehaviour, and an initial weapon.");
            }

            Unload();
            transform.position = spawnPosition;
            combatant.ResetCombatant();
            playerHand.Initialize();
            if (!playerHand.TryEquipWeapon(0, initialWeapon))
            {
                playerHand.Shutdown();
                throw new InvalidOperationException("Failed to equip the initial weapon in slot 0.");
            }

            playerHand.ActivateWeapons();
            IsLoaded = true;
        }

        public void Unload()
        {
            if (playerHand != null)
            {
                playerHand.DeactivateWeapons();
                playerHand.Shutdown();
            }

            IsLoaded = false;
        }

        private void OnDestroy()
        {
            Unload();
        }
    }
}
