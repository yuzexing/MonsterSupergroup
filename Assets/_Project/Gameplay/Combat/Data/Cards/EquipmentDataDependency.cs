using System;
using UnityEngine;

namespace AstralShift.HellMaiden.Data.Cards
{
	[Serializable]
	public class EquipmentDataDependency : DataDependency
	{
		[SerializeField]
		private EquipmentData data;

		public EquipmentData Data => data;
	}
}
