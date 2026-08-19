using System;
using MonsterSupergroup.GAS;
using MonsterSupergroup.GAS.Unity;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Combat
{
    [DisallowMultipleComponent]
    public sealed class PlayerHandBehaviour : MonoBehaviour
    {
        [SerializeField] private Transform attacksRoot;
        [SerializeField] private NearestEnemyTargetProvider targetProvider;
        [SerializeField] private CombatTeamBehaviour owner;

        public PlayerHand Hand { get; private set; }

        public void Configure(
            Transform newAttacksRoot,
            NearestEnemyTargetProvider newTargetProvider,
            CombatTeamBehaviour newOwner)
        {
            attacksRoot = newAttacksRoot ?? throw new ArgumentNullException(nameof(newAttacksRoot));
            targetProvider = newTargetProvider ?? throw new ArgumentNullException(nameof(newTargetProvider));
            owner = newOwner ?? throw new ArgumentNullException(nameof(newOwner));
        }

        public void Initialize(IRandomSource randomSource = null)
        {
            if (attacksRoot == null || targetProvider == null || owner == null)
            {
                throw new InvalidOperationException(
                    "PlayerHandBehaviour requires attacks root, target provider, and owner references.");
            }

            Hand?.Dispose();
            Hand = new PlayerHand(
                attacksRoot,
                targetProvider,
                owner,
                randomSource ?? new UnityRandomSource());
        }

        public bool TryEquipWeapon(int slotIndex, WeaponDefinition definition)
        {
            EnsureInitialized();
            return Hand.TryEquipWeapon(slotIndex, definition);
        }

        public void ActivateWeapons()
        {
            EnsureInitialized();
            Hand.ActivateWeapons();
        }

        public void DeactivateWeapons()
        {
            Hand?.DeactivateWeapons();
        }

        public void Shutdown()
        {
            Hand?.Dispose();
            Hand = null;
        }

        private void EnsureInitialized()
        {
            if (Hand == null)
            {
                throw new InvalidOperationException("PlayerHandBehaviour is not initialized.");
            }
        }

        private void OnDestroy()
        {
            Shutdown();
        }
    }
}
