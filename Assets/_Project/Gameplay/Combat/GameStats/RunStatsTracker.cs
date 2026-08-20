using System;
using System.Collections.Generic;
using System.Linq;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace AstralShift.HellMaiden.GameStats
{
	public class RunStatsTracker : MonoBehaviour
	{
		public static RunStatsTracker Instance;

		private bool _isInitialized;

		public PlayerStatsEntry PlayerStatsEntry { get; private set; }

		public bool RunSucessfull { get; private set; } = true;

		public int Circle { get; set; }

		public uint SignatureWeapon { get; set; }

		public Dictionary<uint, WeaponStatsEntry> WeaponStatsEntries { get; private set; } = new Dictionary<uint, WeaponStatsEntry>();

		public void Awake()
		{
			if (Instance == null)
			{
				Instance = this;
			}
			else
			{
				UnityEngine.Object.Destroy(this);
			}
		}

		public void InitializeRunStats()
		{
			if (!_isInitialized)
			{
				_isInitialized = true;
				RunSucessfull = true;
				SignatureWeapon = PlayerHand.Instance.GetSignatureWeapon().ID;
				CreatePlayerStatsTrackerEntry();
			}
		}

		public void ResetRunStats()
		{
			if (_isInitialized)
			{
				_isInitialized = false;
				PlayerStatsEntry = null;
				WeaponStatsEntries = new Dictionary<uint, WeaponStatsEntry>();
			}
		}

		public void LinkGameEvents()
		{
			if (!_isInitialized)
			{
				return;
			}
			for (int i = 0; i < PlayerHand.Instance.Slots.Count; i++)
			{
				PlayerHandSlot handSlotFromIndex = PlayerHand.Instance.GetHandSlotFromIndex(i);
				RuntimeWeaponData runtimeWeaponData = handSlotFromIndex.RuntimeWeaponData;
				if (runtimeWeaponData != null)
				{
					CreateSlotWeaponStatsTrackerEntry(i, runtimeWeaponData, handSlotFromIndex.WeaponBehaviour);
				}
			}
			PlayerHand.Instance.OnSlotWeaponChanges += CreateSlotWeaponStatsTrackerEntry;
			PlayerStatsEntry?.LinkPlayerEvents();
			GameEvents instance = GameEvents.Instance;
			instance.OnBeforePlayerDeath = (Action)Delegate.Combine(instance.OnBeforePlayerDeath, new Action(RegisterRunFailure));
		}

		public void UnlinkGameEvents()
		{
			PlayerHand.Instance.OnSlotWeaponChanges -= CreateSlotWeaponStatsTrackerEntry;
			PlayerStatsEntry?.CleanLinkedEvents();
			GameEvents instance = GameEvents.Instance;
			instance.OnBeforePlayerDeath = (Action)Delegate.Remove(instance.OnBeforePlayerDeath, new Action(RegisterRunFailure));
		}

		public void RegisterRunFailure()
		{
			RunSucessfull = false;
		}

		private void CreateSlotWeaponStatsTrackerEntry(int position, RuntimeWeaponData weaponData, WeaponBehaviour weapon)
		{
			if (!(weapon == null))
			{
				uint iD = weaponData.Data.ID;
				if (!WeaponStatsEntries.ContainsKey(iD))
				{
					WeaponStatsEntry weaponStatsEntry = new WeaponStatsEntry();
					weaponStatsEntry.SetWeaponId(iD);
					weaponStatsEntry.LinkWeaponEvents(weapon);
					WeaponStatsEntries.Add(iD, weaponStatsEntry);
				}
			}
		}

		public PlayerStatsEntry CreatePlayerStatsTrackerEntry()
		{
			PlayerStatsEntry = new PlayerStatsEntry();
			PlayerStatsEntry.RegisterWeaponEquip(SignatureWeapon);
			return PlayerStatsEntry;
		}

		public void CleanStatsEntriesLinkedEvents()
		{
			PlayerStatsEntry?.CleanLinkedEvents();
			for (int i = 0; i < WeaponStatsEntries.Count; i++)
			{
				WeaponStatsEntries.ElementAt(i).Value.CleanLinkedEvents();
			}
		}

		private void OnDestroy()
		{
			CleanStatsEntriesLinkedEvents();
		}

		public void RegisterWeaponKill(WeaponBehaviour weaponBehaviour)
		{
			if (WeaponStatsEntries.TryGetValue(weaponBehaviour.ID, out var value))
			{
				value.RegisterEnemyDeath();
			}
		}
	}
}
