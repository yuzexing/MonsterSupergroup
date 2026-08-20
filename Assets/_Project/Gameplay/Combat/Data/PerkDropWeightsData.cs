using Sirenix.OdinInspector;
using UnityEngine;

namespace AstralShift.HellMaiden.Data
{
	[CreateAssetMenu(fileName = "New Perk Drop Weights", menuName = "HellMaiden/Data/Perks/Perk Drop Weights")]
	public class PerkDropWeightsData : SerializedScriptableObject
	{
		[SerializeField]
		private PerkDropPerLevelThreshold[] dropWeightsLevelThresholds;

		public int LevelThresholdsCount => dropWeightsLevelThresholds.Length;

		public PerkDropPerLevelThreshold GetPerLevelThresholdDrop(int level)
		{
			return dropWeightsLevelThresholds[level];
		}
	}
}
