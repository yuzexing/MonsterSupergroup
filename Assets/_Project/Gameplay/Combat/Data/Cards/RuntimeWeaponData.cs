using System;

namespace AstralShift.HellMaiden.Data.Cards
{
	[Serializable]
	public class RuntimeWeaponData : RuntimeCardData
	{
		private WeaponData _data;

		public WeaponData Data => _data;

		public RuntimeWeaponData(WeaponData data, uint levelIndex = 0u)
		{
			_data = data;
			_levelIndex = levelIndex;
			_baseData = Data;
		}

		public override object Clone()
		{
			return new RuntimeWeaponData(Data, base.LevelIndex);
		}
	}
}
