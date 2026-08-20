using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Data.Perks;
using UnityEngine;

namespace AstralShift.HellMaiden.Data
{
	[Serializable]
	public struct PerkDropTierWeights
	{
		[Space]
		[SerializeField]
		private PerkDropTier tier;

		[Space]
		[SerializeField]
		private float weight;

		[Space]
		[SerializeField]
		private List<PerkRarityWeight> rarityWeights;

		public PerkDropTier Tier => tier;

		public float Weight => weight;

		public List<PerkRarityWeight> RarityWeights => rarityWeights;

		public HashSet<PerkRarity> SupportedRarities
		{
			get
			{
				HashSet<PerkRarity> hashSet = new HashSet<PerkRarity>();
				foreach (PerkRarityWeight rarityWeight in rarityWeights)
				{
					hashSet.Add(rarityWeight.Rarity);
				}
				return hashSet;
			}
		}

		public PerkDropTierWeights(PerkDropTier tier, float weight, List<PerkRarityWeight> rarityWeights)
		{
			this.tier = tier;
			this.weight = weight;
			this.rarityWeights = rarityWeights;
		}
	}
}
