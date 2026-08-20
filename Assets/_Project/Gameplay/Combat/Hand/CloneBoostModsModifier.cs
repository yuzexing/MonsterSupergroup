using System;
using System.Collections.Generic;
using System.Linq;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[EquipmentModifierType("Clone Boost Cards Modifier")]
	public class CloneBoostModsModifier : StaticStatModifier
	{
		[Serializable]
		[EquipmentModifierParams]
		private class CloneSlotEquipmentsParamsData
		{
			public float percentageMultiplier;

			[Space]
			public EquipmentMultiSlotConfig cloneSlotConfig;
		}

		[InjectEquipmentModifierParams]
		private CloneSlotEquipmentsParamsData _parameters;

		private PlayerHand _handInstance = PlayerHand.Instance;

		protected List<PlayerHandSlot> _fetchSlotsConfigCache = new List<PlayerHandSlot>();

		private Dictionary<PlayerHandSlot, List<RuntimeEquipmentData>> _fetchSlotEquipmentsMap = new Dictionary<PlayerHandSlot, List<RuntimeEquipmentData>>();

		private AttackStatsMultipliers _tempMultipliers;

		private PlayerHandSlot _previousSourceSlot;

		public override void Init(LinkedListNode<PlayerHandSlot> sourceSlotNode)
		{
			base.Init(sourceSlotNode);
			if (_fetchSlotsConfigCache == null)
			{
				_fetchSlotsConfigCache = new List<PlayerHandSlot>();
			}
			if (_handInstance != null)
			{
				_handInstance.OnBeforeHandSlotSwap += ClearCache;
			}
		}

		public override void Dispose()
		{
			if (_handInstance != null)
			{
				_handInstance.OnBeforeHandSlotSwap -= ClearCache;
			}
		}

		public override int GetSortPriority()
		{
			return -2147483647;
		}

		public override void Apply(AttackStatsMultipliers multipliers)
		{
			if (_handInstance == null)
			{
				Debug.LogError("PlayerHand instance not found! Shouldn't happen!");
				return;
			}
			PlayerHandSlot sourceSlot = GetSourceSlot();
			if (sourceSlot != null)
			{
				ClearCache();
				_previousSourceSlot = sourceSlot;
				LinkedListNode<PlayerHandSlot> slotNode = _handInstance.GetSlotNode(sourceSlot);
				CacheStealTargetSlotConfig(slotNode);
				ApplyEffects(multipliers);
			}
		}

		private void ApplyEffects(AttackStatsMultipliers multipliers)
		{
			if (_fetchSlotEquipmentsMap == null)
			{
				_fetchSlotEquipmentsMap = new Dictionary<PlayerHandSlot, List<RuntimeEquipmentData>>();
			}
			_fetchSlotEquipmentsMap.Clear();
			foreach (PlayerHandSlot item in _fetchSlotsConfigCache)
			{
				List<RuntimeEquipmentData> slotBoostEquipments = GetSlotBoostEquipments(item);
				if (slotBoostEquipments.Count > 0)
				{
					_fetchSlotEquipmentsMap.TryAdd(item, slotBoostEquipments);
				}
				for (int i = 0; i < slotBoostEquipments.Count; i++)
				{
					if (_tempMultipliers == null)
					{
						_tempMultipliers = new AttackStatsMultipliers();
					}
					_tempMultipliers.Reset();
					foreach (StaticStatModifier validModifier in GetValidModifiers(slotBoostEquipments[i]))
					{
						validModifier.Apply(_tempMultipliers);
					}
					_tempMultipliers *= _parameters.percentageMultiplier;
					multipliers += _tempMultipliers;
				}
			}
		}

		private void ClearCache()
		{
			if (_fetchSlotsConfigCache == null)
			{
				_fetchSlotsConfigCache = new List<PlayerHandSlot>();
			}
			for (int i = 0; i < _fetchSlotsConfigCache.Count; i++)
			{
				if (_fetchSlotsConfigCache[i] != null)
				{
					_fetchSlotsConfigCache[i].OnEquipmentsChanged -= ReApplyEffect;
				}
			}
			_fetchSlotsConfigCache.Clear();
		}

		private void ReApplyEffect(PlayerHandSlot slot)
		{
			if (_previousSourceSlot != null)
			{
				_previousSourceSlot.RemoveModifier(this);
				_previousSourceSlot.AddModifier(this);
			}
		}

		public void CacheStealTargetSlotConfig(LinkedListNode<PlayerHandSlot> slot)
		{
			if (_parameters.cloneSlotConfig.IsSelfApplied)
			{
				_fetchSlotsConfigCache.Add(slot.Value);
			}
			FindStealTargetSlotConfig(slot, _parameters.cloneSlotConfig.LeftSlots, (LinkedListNode<PlayerHandSlot> currentSlot) => currentSlot.Previous);
			FindStealTargetSlotConfig(slot, _parameters.cloneSlotConfig.RightSlots, (LinkedListNode<PlayerHandSlot> currentSlot) => currentSlot.Next);
			for (int num = 0; num < _fetchSlotsConfigCache.Count; num++)
			{
				_fetchSlotsConfigCache[num].OnEquipmentsChanged += ReApplyEffect;
			}
		}

		private void FindStealTargetSlotConfig(LinkedListNode<PlayerHandSlot> start, EquipmentModifierSlots flags, Func<LinkedListNode<PlayerHandSlot>, LinkedListNode<PlayerHandSlot>> step)
		{
			LinkedListNode<PlayerHandSlot> linkedListNode = start;
			EquipmentModifierSlots[] equipmentModifierSlotsBits = RuntimeEquipmentModifier.EquipmentModifierSlotsBits;
			foreach (EquipmentModifierSlots equipmentModifierSlots in equipmentModifierSlotsBits)
			{
				linkedListNode = step(linkedListNode);
				if (linkedListNode != null)
				{
					if ((flags & equipmentModifierSlots) != EquipmentModifierSlots.None)
					{
						_fetchSlotsConfigCache.Add(linkedListNode.Value);
					}
					continue;
				}
				break;
			}
		}

		private List<RuntimeEquipmentData> GetSlotBoostEquipments(PlayerHandSlot slot)
		{
			return slot.Equipments.Where((RuntimeEquipmentData element) => element.Data.cardType == EquipmentCardType.Normal).ToList();
		}

		private List<StaticStatModifier> GetValidModifiers(RuntimeEquipmentData equipment)
		{
			List<StaticStatModifier> list = new List<StaticStatModifier>();
			foreach (RuntimeEquipmentModifier runtimeModifier in equipment.RuntimeModifiers)
			{
				if (runtimeModifier is StaticStatModifier staticStatModifier && IsValidModifierType(staticStatModifier))
				{
					list.Add(staticStatModifier);
				}
			}
			return list;
		}

		private bool IsValidModifierType(StaticStatModifier modifier)
		{
			return modifier.GetType() != typeof(CloneBoostModsModifier);
		}
	}
}
