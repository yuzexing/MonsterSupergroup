using System;
using AstralShift.HellMaiden.Data.Perks;
using UnityEngine;

namespace AstralShift.HellMaiden.Data
{
	[Serializable]
	public struct PerkRarityWeight
	{
		[Space]
		[SerializeField]
		private PerkRarity rarity;

		[SerializeField]
		private float weight;

		public PerkRarity Rarity => rarity;

		public float Weight => weight;

		public PerkRarityWeight(PerkRarity rarity, float weight)
		{
			this.rarity = rarity;
			this.weight = weight;
		}
	}
}
