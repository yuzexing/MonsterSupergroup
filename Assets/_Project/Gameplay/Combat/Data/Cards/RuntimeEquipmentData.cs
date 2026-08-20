using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Combat.Hand;

namespace AstralShift.HellMaiden.Data.Cards
{
	[Serializable]
	public class RuntimeEquipmentData : RuntimeCardData
	{
		private EquipmentData _data;

		private List<RuntimeEquipmentModifier> _runtimeModifiers;

		public EquipmentData Data => _data;

		public List<RuntimeEquipmentModifier> RuntimeModifiers => _runtimeModifiers;

		public RuntimeEquipmentData(EquipmentData data, uint levelIndex = 0u)
		{
			Refresh(data, levelIndex);
		}

		public void Refresh(EquipmentData data, uint levelIndex)
		{
			_data = data;
			_baseData = _data;
			_levelIndex = levelIndex;
			CreateRuntimeEquipmentModifiers();
		}

		public void CreateRuntimeEquipmentModifiers()
		{
			if (_runtimeModifiers == null)
			{
				_runtimeModifiers = new List<RuntimeEquipmentModifier>();
			}
			else
			{
				_runtimeModifiers.Clear();
			}
			RuntimeEquipmentModifier[] runtimeModifiersFromEquipmentData = RuntimeModifierFactory.Instance.GetRuntimeModifiersFromEquipmentData(_data, _levelIndex);
			if (runtimeModifiersFromEquipmentData != null && runtimeModifiersFromEquipmentData.Length != 0)
			{
				_runtimeModifiers.AddRange(runtimeModifiersFromEquipmentData);
			}
		}

		public override void ApplyLevel(uint levelIndex)
		{
			base.ApplyLevel(levelIndex);
			CreateRuntimeEquipmentModifiers();
		}

		public void IncreaseLevel()
		{
			if (!IsMaxLevel())
			{
				ApplyLevel(base.LevelIndex + 1);
			}
		}

		public override bool IsMaxLevel()
		{
			return base.LevelIndex == Data.Levels.Length - 1;
		}

		public override int GetMaxLevel()
		{
			return Data.Levels.Length;
		}

		public override object Clone()
		{
			return new RuntimeEquipmentData(Data, base.LevelIndex);
		}

		public static bool CanMerge(RuntimeEquipmentData data1, RuntimeEquipmentData data2)
		{
			if (data1 == null || data2 == null)
			{
				return false;
			}
			if (data1.IsMaxLevel() || data2.IsMaxLevel())
			{
				return false;
			}
			if (data1.Data == data2.Data)
			{
				return data1.LevelIndex == data2.LevelIndex;
			}
			return false;
		}
	}
}
