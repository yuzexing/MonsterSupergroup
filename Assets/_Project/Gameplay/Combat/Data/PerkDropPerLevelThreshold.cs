using System;
using UnityEngine;

namespace AstralShift.HellMaiden.Data
{
	[Serializable]
	public struct PerkDropPerLevelThreshold
	{
		[SerializeField]
		private int level;

		[SerializeField]
		private PerkDropTierWeights[] dropWeights;

		public int Level => level;

		public PerkDropTierWeights[] Drop => dropWeights;

		public PerkDropPerLevelThreshold(int level, PerkDropTierWeights[] dropWeights)
		{
			this.level = level;
			this.dropWeights = dropWeights;
		}
	}
}
