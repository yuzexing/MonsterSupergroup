using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Data.Perks;
using AstralShift.HellMaiden.Data.Shrines;
using AstralShift.HellMaiden.DevDebug;
using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.HellMaiden.Scenes;

namespace AstralShift.HellMaiden.Combat.Hand
{
	public class PlayerHand
	{
		private static PlayerHand _instance;

		private LinkedList<PlayerHandSlot> _slots;

		public static readonly uint HANDSLOTS_COUNT = 4u;

		public static readonly uint MAX_EQUIPS_PER_SLOT = 3u;

		public static readonly uint MAX_EQUIPMENT = MAX_EQUIPS_PER_SLOT * HANDSLOTS_COUNT;

		private WeaponData _signatureWeaponData;

		protected RuntimeWeaponData _signatureWeapon;

		private StringBuilder _debugHandStringBuilder = new StringBuilder();

		public static PlayerHand Instance
		{
			get
			{
				if (_instance == null)
				{
					_instance = new PlayerHand();
				}
				return _instance;
			}
		}

		public LinkedList<PlayerHandSlot> Slots => _slots;

		public int WeaponCount
		{
			get
			{
				int num = 0;
				for (int i = 0; i < _slots.Count; i++)
				{
					num += (GetHandSlotFromIndex(i).HasWeapon() ? 1 : 0);
				}
				return num;
			}
		}

		public int EquipmentCount
		{
			get
			{
				int num = 0;
				for (int i = 0; i < _slots.Count; i++)
				{
					num += GetHandSlotFromIndex(i).Equipments.Count;
				}
				return num;
			}
		}

		public bool IsHandFull => EquipmentCount >= MAX_EQUIPMENT;

		public bool MaxWeaponsReached => WeaponCount == _slots.Count;

		public Dictionary<PerkPoolID, List<RuntimePerk>> Perks { get; private set; }

		public List<RuntimePerk> PerksList => Perks.SelectMany((KeyValuePair<PerkPoolID, List<RuntimePerk>> x) => x.Value).ToList();

		public List<RuntimeShrine> PermanentShrines { get; private set; }

		public List<RuntimeShrine> TemporaryShrines { get; private set; }

		public List<RuntimeShrine> AllShrines => PermanentShrines.Concat(TemporaryShrines).ToList();

		public event Action<int, RuntimeWeaponData, WeaponBehaviour> OnSlotWeaponChanges;

		public event Action<RuntimePerk> OnPerkAdded;

		public event Action<RuntimeShrine> OnPermanentShrineAdded;

		public event Action<RuntimeShrine> OnTemporaryShrineAdded;

		public event Action<RuntimeShrine> OnTemporaryShrineRemoved;

		public event Action OnBeforeHandSlotSwap;

		public event Action OnAfterHandSlotSwap;

		public event Action OnReset;

		public void Init()
		{
			SceneMaster.Instance.OnSceneHideFinishPersist += DeactivateWeapons;
			_slots = new LinkedList<PlayerHandSlot>();
			for (int i = 0; i < HANDSLOTS_COUNT; i++)
			{
				PlayerHandSlot value = new PlayerHandSlot(this);
				_slots.AddLast(new LinkedListNode<PlayerHandSlot>(value));
			}
			GetSignatureWeaponSaveData();
			Perks = new Dictionary<PerkPoolID, List<RuntimePerk>>();
			PermanentShrines = new List<RuntimeShrine>();
			TemporaryShrines = new List<RuntimeShrine>();
		}

		public void Reset()
		{
			if (_slots == null)
			{
				_slots = new LinkedList<PlayerHandSlot>();
			}
			_slots.Clear();
			for (int i = 0; i < HANDSLOTS_COUNT; i++)
			{
				PlayerHandSlot value = new PlayerHandSlot(this);
				_slots.AddLast(new LinkedListNode<PlayerHandSlot>(value));
			}
			ClearAll();
			GetSignatureWeaponSaveData();
		}

		private void Dispose()
		{
			ClearAll();
			for (int i = 0; i < _slots.Count; i++)
			{
				Slots.ElementAt(i).Dispose();
			}
		}

		public void ClearWeapons()
		{
			if (_slots != null)
			{
				for (int num = Slots.Count - 1; num >= 0; num--)
				{
					Slots.ElementAt(num).ClearWeapon();
				}
			}
		}

		public void ClearEquipments(int slotIndex)
		{
			Slots.ElementAt(slotIndex).ClearEquipments();
		}

		public void ClearAllEquipments()
		{
			for (int num = Slots.Count - 1; num >= 0; num--)
			{
				ClearEquipments(num);
			}
		}

		public void ClearAllPerks()
		{
			if (Perks == null)
			{
				Dictionary<PerkPoolID, List<RuntimePerk>> dictionary = (Perks = new Dictionary<PerkPoolID, List<RuntimePerk>>());
			}
			Perks.Clear();
			GameDirector.Instance.Player.PlayerStats.RemoveAllModifiers();
		}

		public void ClearAllShrines()
		{
			PermanentShrines.Clear();
			foreach (RuntimeShrine temporaryShrine in TemporaryShrines)
			{
				temporaryShrine.CancelTimeout();
			}
			TemporaryShrines.Clear();
		}

		public void ClearAll()
		{
			ClearAllEquipments();
			ClearWeapons();
			ClearAllPerks();
			ClearAllShrines();
			this.OnReset?.Invoke();
		}

		public void ActivateWeapons()
		{
			foreach (PlayerHandSlot slot in Slots)
			{
				slot.ActivateWeapon();
			}
		}

		public void DeactivateWeapons()
		{
			foreach (PlayerHandSlot slot in Slots)
			{
				slot.DeactivateWeapon();
			}
		}

		public void RegisterWeaponChanges()
		{
			for (int i = 0; i < Slots.Count; i++)
			{
				PlayerHandSlot handSlotFromIndex = GetHandSlotFromIndex(i);
				if (handSlotFromIndex.RuntimeWeaponData == null)
				{
					this.OnSlotWeaponChanges?.Invoke(i, null, handSlotFromIndex.WeaponBehaviour);
				}
				else
				{
					this.OnSlotWeaponChanges?.Invoke(i, handSlotFromIndex.RuntimeWeaponData, handSlotFromIndex.WeaponBehaviour);
				}
			}
		}

		public PlayerHandSlot GetHandSlotFromIndex(int index)
		{
			return _slots.ElementAt(index);
		}

		public LinkedListNode<PlayerHandSlot> GetSlotNode(PlayerHandSlot slot)
		{
			return _slots.Find(slot);
		}

		public int GetSlotIndex(PlayerHandSlot slot)
		{
			LinkedListNode<PlayerHandSlot> slotNode = GetSlotNode(slot);
			if (slotNode == null || slotNode.List == null)
			{
				return -1;
			}
			int num = 0;
			for (LinkedListNode<PlayerHandSlot> previous = slotNode.Previous; previous != null; previous = previous.Previous)
			{
				num++;
			}
			return num;
		}

		public void MoveAfter(int index)
		{
			PlayerHandSlot handSlotFromIndex = GetHandSlotFromIndex(index);
			LinkedListNode<PlayerHandSlot> slotNode = GetSlotNode(handSlotFromIndex);
			LinkedListNode<PlayerHandSlot> next = slotNode.Next;
			if (next != null)
			{
				this.OnBeforeHandSlotSwap?.Invoke();
				Slots.Remove(slotNode);
				Slots.AddAfter(next, new LinkedListNode<PlayerHandSlot>(slotNode.Value));
				this.OnAfterHandSlotSwap?.Invoke();
				RegisterWeaponChanges();
			}
		}

		public void MoveBefore(int index)
		{
			PlayerHandSlot handSlotFromIndex = GetHandSlotFromIndex(index);
			LinkedListNode<PlayerHandSlot> slotNode = GetSlotNode(handSlotFromIndex);
			LinkedListNode<PlayerHandSlot> previous = slotNode.Previous;
			if (previous != null)
			{
				this.OnBeforeHandSlotSwap?.Invoke();
				Slots.Remove(slotNode);
				Slots.AddBefore(previous, new LinkedListNode<PlayerHandSlot>(slotNode.Value));
				this.OnAfterHandSlotSwap?.Invoke();
				RegisterWeaponChanges();
			}
		}

		private void GetSignatureWeaponSaveData()
		{
			uint signatureWeaponID = GameDataManager.Instance.GetSignatureWeaponID();
			WeaponData weaponData = GameDirector.Instance.runtimeDB.GetWeaponData(signatureWeaponID);
			SetSignatureWeapon(weaponData);
		}

		public bool TryEquipSignatureWeapon()
		{
			if (DeveloperDebug.CardTester_OverrideSignatureWeapon)
			{
				SetSignatureWeapon(DeveloperDebug.CardTester_SignatureWeapon);
			}
			if (!_signatureWeaponData)
			{
				return false;
			}
			foreach (PlayerHandSlot slot in Slots)
			{
				if (!slot.HasWeapon())
				{
					_signatureWeapon = new RuntimeWeaponData(_signatureWeaponData);
					slot.AddWeapon(_signatureWeapon, isDeactivated: true);
					Leveler.Instance.CardPool.RegisterSignatureWeapon(_signatureWeapon);
					return true;
				}
			}
			return false;
		}

		public void SetSignatureWeapon(WeaponData data)
		{
			_signatureWeaponData = data;
		}

		public WeaponData GetSignatureWeapon()
		{
			return _signatureWeaponData;
		}

		public bool TryGetEnqueuedSignatureWeapon(out WeaponData data)
		{
			data = _signatureWeaponData;
			return data;
		}

		public bool TryGetEquippedSignatureWeapon(out RuntimeWeaponData data)
		{
			data = _signatureWeapon;
			return data?.Data;
		}

		public void ClearSignatureWeapon()
		{
			_signatureWeaponData = null;
		}

		public void AddPerk(PerkPoolID perkPoolID, RuntimePerkData runtimePerkData)
		{
			if (!Perks.ContainsKey(perkPoolID))
			{
				Perks.Add(perkPoolID, new List<RuntimePerk>());
			}
			if (TryGetPerk(runtimePerkData, out var foundPerk))
			{
				foundPerk.Upgrade(runtimePerkData);
			}
			else
			{
				foundPerk = new RuntimePerk(runtimePerkData);
				Perks[perkPoolID].Add(foundPerk);
			}
			this.OnPerkAdded?.Invoke(foundPerk);
			foreach (PlayerHandSlot slot in Slots)
			{
				slot.UpdateWeaponBehaviour();
			}
		}

		public bool TryGetAllPerks(out List<RuntimePerk> perks)
		{
			perks = new List<RuntimePerk>();
			foreach (PerkPoolID key in Perks.Keys)
			{
				Perks.TryGetValue(key, out var value);
				if (value != null)
				{
					perks.AddRange(value);
				}
			}
			return perks.Count > 0;
		}

		public bool TryGetPerk(RuntimePerkData runtimePerkData, out RuntimePerk foundPerk)
		{
			if (TryGetAllPerks(out var perks))
			{
				foreach (RuntimePerk item in perks)
				{
					if (runtimePerkData.Data.ID == item.RuntimeData.Data.ID)
					{
						foundPerk = item;
						return true;
					}
				}
			}
			foundPerk = null;
			return false;
		}

		public bool TryGetPerk(uint id, PerkRarity rarity, out RuntimePerk perk)
		{
			if (TryGetAllPerks(out var perks))
			{
				foreach (RuntimePerk item in perks)
				{
					if (item.RuntimeData.Data.ID == id && item.RuntimeData.Rarity == rarity)
					{
						perk = item;
						return true;
					}
				}
			}
			perk = null;
			return false;
		}

		public bool TryGetPerkByModifierID(PerkModifierID perModifierID, out RuntimePerk resultPerk)
		{
			resultPerk = PerksList.FirstOrDefault((RuntimePerk runtimePerk) => runtimePerk.RuntimeData.Data.GetAllRarities().Any((PerkRarityModifiersData element) => element.Modifiers.Any((PerkDataModifier modifier) => modifier.ModifierID.Equals(perModifierID))));
			return resultPerk != null;
		}

		public void ApplyShrine(ShrineData data)
		{
			if (data.permanent)
			{
				AddPermanentShrine(data);
			}
			else
			{
				AddTemporaryShrine(data);
			}
		}

		private void AddPermanentShrine(ShrineData data)
		{
			RuntimeShrine runtimeShrine = PermanentShrines.FirstOrDefault((RuntimeShrine element) => element.ShrineData == data);
			if (runtimeShrine != null)
			{
				runtimeShrine.Add();
				this.OnPermanentShrineAdded?.Invoke(runtimeShrine);
				return;
			}
			runtimeShrine = new RuntimeShrine(data);
			runtimeShrine.Add();
			PermanentShrines.Add(runtimeShrine);
			this.OnPermanentShrineAdded?.Invoke(runtimeShrine);
		}

		private void AddTemporaryShrine(ShrineData data)
		{
			RuntimeShrine runtimeShrine = new RuntimeShrine(data);
			runtimeShrine.AddTemporary(RemoveTemporaryShrine);
			TemporaryShrines.Add(runtimeShrine);
			this.OnTemporaryShrineAdded?.Invoke(runtimeShrine);
		}

		private void RemoveTemporaryShrine(RuntimeShrine runtimeShrine)
		{
			TemporaryShrines.Remove(runtimeShrine);
			this.OnTemporaryShrineRemoved?.Invoke(runtimeShrine);
		}

		public bool TryGetShrineByModifierID(PerkModifierID shrineID, out RuntimeShrine resultShrine)
		{
			foreach (RuntimeShrine allShrine in AllShrines)
			{
				if (allShrine.ModifiersCount != 0 && allShrine.ShrineData.Modifiers.Any((PerkDataModifier modifier) => modifier.ModifierID.Equals(shrineID)))
				{
					resultShrine = allShrine;
					return true;
				}
			}
			resultShrine = null;
			return false;
		}

		public void DebugLogHand()
		{
		}

		private string GetHandSlotInfo(PlayerHandSlot slot)
		{
			string empty = string.Empty;
			if (!slot.HasWeapon())
			{
				return empty + "Weapon: None";
			}
			empty += $"Weapon: {slot.RuntimeWeaponData.Data.GetTitle()} / ID: {slot.RuntimeWeaponData.Data.ID}\n";
			if (!slot.HasEquipments())
			{
				return empty + "Equipments: None";
			}
			empty += "Equipments:\n";
			for (int i = 0; i < slot.Equipments.Count; i++)
			{
				EquipmentData data = slot.Equipments[i].Data;
				empty += $"- {data.GetTitle()} / ID: {data.ID}";
				if (i < slot.Equipments.Count - 1)
				{
					empty += "\n";
				}
			}
			return empty;
		}
	}
}
