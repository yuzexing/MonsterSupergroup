using System;
using System.Collections.Generic;
using MonsterSupergroup.GAS;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Combat
{
    public sealed class PlayerHand : IDisposable
    {
        public const int SlotCount = 4;
        public const int MaxEquipmentPerSlot = 3;

        private readonly PlayerHandSlot[] slots;
        private readonly GameObject[] slotRoots;

        public PlayerHand(
            Transform attacksRoot,
            NearestEnemyTargetProvider targetProvider,
            CombatTeamBehaviour owner,
            IRandomSource randomSource,
            CombatRuntimeServices runtimeServices = null)
        {
            if (attacksRoot == null)
            {
                throw new ArgumentNullException(nameof(attacksRoot));
            }

            if (targetProvider == null)
            {
                throw new ArgumentNullException(nameof(targetProvider));
            }

            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            if (randomSource == null)
            {
                throw new ArgumentNullException(nameof(randomSource));
            }

            slots = new PlayerHandSlot[SlotCount];
            slotRoots = new GameObject[SlotCount];
            for (int i = 0; i < slots.Length; i++)
            {
                GameObject slotObject = new GameObject($"WeaponSlot{i}");
                slotRoots[i] = slotObject;
                Transform slotRoot = slotObject.transform;
                slotRoot.SetParent(attacksRoot, false);
                slots[i] = new PlayerHandSlot(
                    this,
                    i,
                    slotRoot,
                    targetProvider,
                    owner,
                    randomSource,
                    runtimeServices);
            }
        }

        public IReadOnlyList<PlayerHandSlot> Slots => slots;
        public bool IsActive { get; private set; }

        public event Action<int, PlayerHandSlot> SlotChanged;

        public bool TryEquipWeapon(int slotIndex, WeaponDefinition definition)
        {
            return GetSlot(slotIndex).TryEquipWeapon(definition);
        }

        public bool TryUnequipWeapon(int slotIndex)
        {
            return GetSlot(slotIndex).ClearWeapon();
        }

        public PlayerHandSlot GetSlot(int slotIndex)
        {
            if ((uint)slotIndex >= slots.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(slotIndex));
            }

            return slots[slotIndex];
        }

        public void ActivateWeapons()
        {
            IsActive = true;
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i].ActivateWeapon();
            }
        }

        public void DeactivateWeapons()
        {
            IsActive = false;
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i].DeactivateWeapon();
            }
        }

        public void Clear()
        {
            IsActive = false;
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i].ClearWeapon(false);
            }
        }

        internal void NotifySlotChanged(PlayerHandSlot slot)
        {
            SlotChanged?.Invoke(slot.Index, slot);
        }

        public void ConfigureCombatRuntimeServices(CombatRuntimeServices services)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            for (int i = 0; i < slots.Length; i++)
            {
                slots[i].ConfigureCombatRuntimeServices(services);
            }
        }

        public void Dispose()
        {
            Clear();
            for (int i = 0; i < slots.Length; i++)
            {
                if (slotRoots[i] != null)
                {
                    UnityEngine.Object.Destroy(slotRoots[i]);
                }
            }

            SlotChanged = null;
        }
    }
}
