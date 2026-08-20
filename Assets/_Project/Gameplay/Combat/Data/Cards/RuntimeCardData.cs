using System;

namespace AstralShift.HellMaiden.Data.Cards
{
	[Serializable]
	public abstract class RuntimeCardData : IComparable<RuntimeCardData>, ICloneable
	{
		protected CardData _baseData;

		protected uint _levelIndex;

		public CardData BaseData => _baseData;

		public uint LevelIndex => _levelIndex;

		public virtual void ApplyLevel(uint levelIndex)
		{
			_levelIndex = levelIndex;
		}

		public virtual bool IsMaxLevel()
		{
			return true;
		}

		public virtual int GetMaxLevel()
		{
			return 1;
		}

		public int CompareTo(RuntimeCardData other)
		{
			if (other.BaseData == BaseData && other.LevelIndex == LevelIndex)
			{
				return 1;
			}
			return 0;
		}

		public abstract object Clone();
	}
}
