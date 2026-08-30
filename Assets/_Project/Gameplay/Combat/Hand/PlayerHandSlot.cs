using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[Serializable]
	public class PlayerHandSlot : IDisposable
	{
		private PlayerHand _hand;

		private List<RuntimeEquipmentData> _equipments;

		private RuntimeEquipmentModifiers _equipmentModifiers;

		public WeaponBehaviour WeaponBehaviour { get; set; }

		public RuntimeWeaponData RuntimeWeaponData { get; set; }

		public List<RuntimeEquipmentData> Equipments => _equipments;

		public RuntimeEquipmentModifiers EquipmentModifiers => _equipmentModifiers;

		public event Action<PlayerHandSlot> OnEquipmentsChanged;

		public PlayerHandSlot(PlayerHand hand)
		{
			_hand = hand;
			_equipments = new List<RuntimeEquipmentData>();
			_equipmentModifiers = new RuntimeEquipmentModifiers();
			hand.OnAfterHandSlotSwap += ReApplyMultiSlotModifiers;
			hand.OnBeforeHandSlotSwap += RemoveMultiSlotModifiers;
		}

		public void Dispose()
		{
			_hand.OnAfterHandSlotSwap -= ReApplyMultiSlotModifiers;
			_hand.OnBeforeHandSlotSwap -= RemoveMultiSlotModifiers;
		}

		public bool HasWeapon()
		{
			return WeaponBehaviour;
		}

		public bool HasEquipments()
		{
			return Equipments.Count > 0;
		}

		public bool IsEquipmentCompatible(RuntimeEquipmentData runtimeData)
		{
			if (!HasWeapon())
			{
				return true;
			}
			if (runtimeData.Data.cardType == EquipmentCardType.Multi)
			{
				return true;
			}
			int modifierFlags = (int)RuntimeWeaponData.Data.modifierFlags;
			return ((uint)runtimeData.Data.usedStatsModifiers & (uint)modifierFlags) != 0;
		}

		public void AddWeapon(RuntimeWeaponData runtimeData, bool isDeactivated = false)
		{
			if (runtimeData?.Data != null && runtimeData.Data.UsesNativeGasRuntime)
			{
				throw new InvalidOperationException(
					"New GAS native weapons must be equipped by the owning PlayerBuildRuntime, not the legacy singleton PlayerHand.");
			}

			WeaponBehaviour weaponBehaviour = UnityEngine.Object.Instantiate(runtimeData.Data.WeaponPrefab, GameDirector.Instance.Player.AttacksParent);
			weaponBehaviour.Init(runtimeData.Data.ID, runtimeData.Data.BaseStats);
			WeaponBehaviour = weaponBehaviour;
			RuntimeWeaponData = runtimeData;
			_hand.RegisterWeaponChanges();
			UpdateWeaponBehaviour();
			if (isDeactivated)
			{
				DeactivateWeapon();
			}
			GameEvents.Instance.OnWeaponAdded?.Invoke(weaponBehaviour);
			_hand.DebugLogHand();
		}

		public void ActivateWeapon()
		{
			if ((bool)WeaponBehaviour)
			{
				WeaponBehaviour.Activate();
				WeaponBehaviour.Init(RuntimeWeaponData.Data.ID, RuntimeWeaponData.Data.BaseStats);
				_hand.RegisterWeaponChanges();
				UpdateWeaponBehaviour();
			}
		}

		public void DeactivateWeapon()
		{
			if ((bool)WeaponBehaviour)
			{
				WeaponBehaviour.Deactivate();
			}
		}

		public bool ContainsEquipment(RuntimeEquipmentData equipmentData)
		{
			foreach (RuntimeEquipmentData equipment in _equipments)
			{
				if (equipment == equipmentData)
				{
					return true;
				}
			}
			return false;
		}

		public RuntimeEquipmentData GetMergeableEquipmentInSlot(EquipmentData equipmentData, uint levelIndex)
		{
			foreach (RuntimeEquipmentData equipment in _equipments)
			{
				if (equipment.LevelIndex < equipment.Data.Levels.Length - 1 && equipment.Data == equipmentData && equipment.LevelIndex == levelIndex)
				{
					return equipment;
				}
			}
			return null;
		}

		public int GetPotentialMergeCount(RuntimeEquipmentData toMergeRuntimeData)
		{
			int num = 0;
			EquipmentData data = toMergeRuntimeData.Data;
			uint levelIndex = toMergeRuntimeData.LevelIndex;
			if (ContainsEquipment(toMergeRuntimeData))
			{
				return num;
			}
			while (true)
			{
				RuntimeEquipmentData mergeableEquipmentInSlot = GetMergeableEquipmentInSlot(data, levelIndex);
				if (mergeableEquipmentInSlot == null)
				{
					break;
				}
				num++;
				data = mergeableEquipmentInSlot.Data;
				levelIndex = mergeableEquipmentInSlot.LevelIndex + 1;
			}
			return num;
		}

		public void AddEquipment(RuntimeEquipmentData equipment)
		{
			if (equipment == null)
			{
				return;
			}
			_equipments.Add(equipment);
			foreach (RuntimeEquipmentModifier runtimeModifier in equipment.RuntimeModifiers)
			{
				AddModifier(runtimeModifier);
			}
			this.OnEquipmentsChanged?.Invoke(this);
			_hand.DebugLogHand();
		}

		public void AddModifier(RuntimeEquipmentModifier runtimeModifier)
		{
			runtimeModifier.Init(_hand.GetSlotNode(this));
			if (runtimeModifier.HasMultiSlotConfig && !runtimeModifier.IsMultiSlotApplied())
			{
				_equipmentModifiers.MultiSlotModifiers.Add(runtimeModifier);
				runtimeModifier.ApplyMultiSlot();
				if (!runtimeModifier.IsSelfApplied)
				{
					UpdateWeaponBehaviour();
					return;
				}
			}
			if (!(runtimeModifier is StaticStatModifier item))
			{
				if (!(runtimeModifier is DynamicStatModifier item2))
				{
					if (!(runtimeModifier is OnHitModifier item3))
					{
						if (!(runtimeModifier is OnKillModifier item4))
						{
							if (runtimeModifier is DynamicOnDamageModifier item5)
							{
								_equipmentModifiers.DynamicOnDamageModifiers.Add(item5);
							}
						}
						else
						{
							_equipmentModifiers.OnKillModifiers.Add(item4);
							_equipmentModifiers.OnKillModifiers.Sort(OnKillModifierPriorityComparer.Instance);
						}
					}
					else
					{
						_equipmentModifiers.OnHitModifiers.Add(item3);
						_equipmentModifiers.OnHitModifiers.Sort(OnHitModifierPriorityComparer.Instance);
					}
				}
				else
				{
					_equipmentModifiers.DynamicModifiers.Add(item2);
					_equipmentModifiers.DynamicModifiers.Sort(DynamicStatModifierPriorityComparer.Instance);
				}
			}
			else
			{
				_equipmentModifiers.StaticModifiers.Add(item);
				_equipmentModifiers.StaticModifiers.Sort(StaticStatModifierPriorityComparer.Instance);
			}
			UpdateWeaponBehaviour();
		}

		public void RemoveEquipment(RuntimeEquipmentData equipment)
		{
			if (equipment != null)
			{
				_equipments.Remove(equipment);
				RuntimeEquipmentModifier[] array = equipment.RuntimeModifiers.ToArray();
				for (int num = array.Length - 1; num >= 0; num--)
				{
					RuntimeEquipmentModifier runtimeModifier = array[num];
					RemoveModifier(runtimeModifier);
				}
				this.OnEquipmentsChanged?.Invoke(this);
			}
		}

		public void RemoveModifier(RuntimeEquipmentModifier runtimeModifier)
		{
			if (runtimeModifier.HasMultiSlotConfig && runtimeModifier.IsSourceSlot(this))
			{
				_equipmentModifiers.MultiSlotModifiers.Remove(runtimeModifier);
				runtimeModifier.Dispose();
				runtimeModifier.RemoveMultiSlotModifiers();
				if (!runtimeModifier.IsSelfApplied)
				{
					UpdateWeaponBehaviour();
					return;
				}
			}
			if (!(runtimeModifier is StaticStatModifier staticStatModifier))
			{
				if (!(runtimeModifier is DynamicStatModifier dynamicStatModifier))
				{
					if (!(runtimeModifier is OnHitModifier onHitModifier))
					{
						if (!(runtimeModifier is OnKillModifier onKillModifier))
						{
							if (runtimeModifier is DynamicOnDamageModifier item)
							{
								_equipmentModifiers.DynamicOnDamageModifiers.Remove(item);
							}
						}
						else
						{
							_equipmentModifiers.OnKillModifiers.Remove(onKillModifier);
							_equipmentModifiers.OnKillModifiers.Sort(OnKillModifierPriorityComparer.Instance);
							onKillModifier.Dispose();
						}
					}
					else
					{
						_equipmentModifiers.OnHitModifiers.Remove(onHitModifier);
						_equipmentModifiers.OnHitModifiers.Sort(OnHitModifierPriorityComparer.Instance);
						onHitModifier.Dispose();
					}
				}
				else
				{
					_equipmentModifiers.DynamicModifiers.Remove(dynamicStatModifier);
					_equipmentModifiers.DynamicModifiers.Sort(DynamicStatModifierPriorityComparer.Instance);
					dynamicStatModifier.Dispose();
				}
			}
			else
			{
				_equipmentModifiers.StaticModifiers.Remove(staticStatModifier);
				_equipmentModifiers.StaticModifiers.Sort(StaticStatModifierPriorityComparer.Instance);
				staticStatModifier.Dispose();
			}
			UpdateWeaponBehaviour();
		}

		public void RemoveMultiSlotModifiers()
		{
			for (int num = _equipmentModifiers.MultiSlotModifiers.Count - 1; num >= 0; num--)
			{
				RuntimeEquipmentModifier runtimeModifier = _equipmentModifiers.MultiSlotModifiers[num];
				RemoveModifier(runtimeModifier);
			}
		}

		public void ReApplyMultiSlotModifiers()
		{
			RemoveMultiSlotModifiers();
			foreach (RuntimeEquipmentData equipment in _equipments)
			{
				RuntimeEquipmentModifier[] array = equipment.RuntimeModifiers.FindAll((RuntimeEquipmentModifier modifier) => modifier.HasMultiSlotConfig).ToArray();
				if (array.Length != 0)
				{
					RuntimeEquipmentModifier[] array2 = array;
					foreach (RuntimeEquipmentModifier runtimeModifier in array2)
					{
						AddModifier(runtimeModifier);
					}
				}
			}
		}

		public void ClearWeapon()
		{
			if (WeaponBehaviour != null)
			{
				WeaponBehaviour.Deactivate();
				UnityEngine.Object.Destroy(WeaponBehaviour.gameObject);
			}
			if (RuntimeWeaponData != null)
			{
				RuntimeWeaponData = null;
			}
		}

		public void ClearEquipments()
		{
			for (int i = 0; i < Equipments.Count; i++)
			{
				RemoveEquipment(Equipments[i]);
			}
			Equipments.Clear();
			_equipmentModifiers = new RuntimeEquipmentModifiers();
			UpdateWeaponBehaviour();
		}

		public void UpdateWeaponBehaviour()
		{
			if (!(WeaponBehaviour == null))
			{
				WeaponBehaviour.UpdateModifiers(_equipmentModifiers);
			}
		}
	}
}
