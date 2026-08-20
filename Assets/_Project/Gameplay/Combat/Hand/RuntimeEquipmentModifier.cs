using System;
using System.Collections.Generic;
using System.Linq;
using AstralShift.HellMaiden.Data;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[Serializable]
	public abstract class RuntimeEquipmentModifier
	{
		private uint _id;

		protected bool _hasMultiSlotConfig;

		protected bool _isSelfApplied;

		protected EquipmentModifierSlots _leftSlots;

		protected EquipmentModifierSlots _rightSlots;

		protected static readonly EquipmentModifierSlots[] EquipmentModifierSlotsBits = (from EquipmentModifierSlots value in Enum.GetValues(typeof(EquipmentModifierSlots))
			where value != EquipmentModifierSlots.None && IsPowerOfTwo((int)value)
			orderby (int)value
			select value).ToArray();

		protected LinkedListNode<PlayerHandSlot> _sourceSlotNode;

		protected LinkedListNode<PlayerHandSlot> _multiSlotSourceNode;

		private bool _isMultiSlotApplied;

		protected List<PlayerHandSlot> _multiSlotConfigCache;

		public uint ID
		{
			get
			{
				return _id;
			}
			set
			{
				_id = value;
			}
		}

		public bool HasMultiSlotConfig
		{
			get
			{
				return _hasMultiSlotConfig;
			}
			set
			{
				_hasMultiSlotConfig = value;
			}
		}

		public bool IsSelfApplied
		{
			get
			{
				return _isSelfApplied;
			}
			set
			{
				_isSelfApplied = value;
			}
		}

		public EquipmentModifierSlots LeftSlots
		{
			get
			{
				return _leftSlots;
			}
			set
			{
				_leftSlots = value;
			}
		}

		public EquipmentModifierSlots RightSlots
		{
			get
			{
				return _rightSlots;
			}
			set
			{
				_rightSlots = value;
			}
		}

		protected static bool IsPowerOfTwo(int x)
		{
			return (x & (x - 1)) == 0;
		}

		public virtual int GetSortPriority()
		{
			return 1;
		}

		public bool IsSourceSlot(PlayerHandSlot slot)
		{
			if (!IsMultiSlotApplied())
			{
				return _sourceSlotNode.Value == slot;
			}
			return _multiSlotSourceNode.Value == slot;
		}

		public PlayerHandSlot GetSourceSlot()
		{
			if (!IsMultiSlotApplied())
			{
				return _sourceSlotNode.Value;
			}
			return _multiSlotSourceNode.Value;
		}

		public bool IsMultiSlotApplied()
		{
			return _isMultiSlotApplied;
		}

		public virtual void Init(LinkedListNode<PlayerHandSlot> sourceSlotNode)
		{
			_sourceSlotNode = sourceSlotNode;
		}

		public virtual void ApplyMultiSlot()
		{
			if (_hasMultiSlotConfig)
			{
				_isMultiSlotApplied = true;
				_multiSlotSourceNode = _sourceSlotNode;
				if (_multiSlotConfigCache == null)
				{
					_multiSlotConfigCache = new List<PlayerHandSlot>();
				}
				else
				{
					_multiSlotConfigCache.Clear();
				}
				CacheMultiSlotConfig(_sourceSlotNode);
				AddMultiSlotModifiers();
			}
		}

		public void CacheMultiSlotConfig(LinkedListNode<PlayerHandSlot> slot)
		{
			FindMultiSlotConfig(slot, LeftSlots, (LinkedListNode<PlayerHandSlot> currentSlot) => currentSlot.Previous);
			FindMultiSlotConfig(slot, RightSlots, (LinkedListNode<PlayerHandSlot> currentSlot) => currentSlot.Next);
		}

		private void FindMultiSlotConfig(LinkedListNode<PlayerHandSlot> start, EquipmentModifierSlots flags, Func<LinkedListNode<PlayerHandSlot>, LinkedListNode<PlayerHandSlot>> step)
		{
			LinkedListNode<PlayerHandSlot> linkedListNode = start;
			EquipmentModifierSlots[] equipmentModifierSlotsBits = EquipmentModifierSlotsBits;
			foreach (EquipmentModifierSlots equipmentModifierSlots in equipmentModifierSlotsBits)
			{
				linkedListNode = step(linkedListNode);
				if (linkedListNode != null)
				{
					if ((flags & equipmentModifierSlots) != EquipmentModifierSlots.None)
					{
						_multiSlotConfigCache.Add(linkedListNode.Value);
					}
					continue;
				}
				break;
			}
		}

		public virtual void AddMultiSlotModifiers()
		{
			for (int i = 0; i < _multiSlotConfigCache.Count; i++)
			{
				_multiSlotConfigCache[i].AddModifier(this);
			}
		}

		public virtual void RemoveMultiSlotModifiers()
		{
			if (_multiSlotConfigCache != null)
			{
				for (int num = _multiSlotConfigCache.Count - 1; num >= 0; num--)
				{
					_multiSlotConfigCache[num].RemoveModifier(this);
				}
				_multiSlotConfigCache.Clear();
				_multiSlotSourceNode = null;
				_isMultiSlotApplied = false;
			}
		}

		public virtual void Dispose()
		{
		}
	}
}
