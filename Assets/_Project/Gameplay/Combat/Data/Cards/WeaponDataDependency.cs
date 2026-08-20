using System;
using UnityEngine;

namespace AstralShift.HellMaiden.Data.Cards
{
	[Serializable]
	public class WeaponDataDependency : DataDependency
	{
		[SerializeField]
		private WeaponData data;

		public WeaponData Data => data;
	}
}
