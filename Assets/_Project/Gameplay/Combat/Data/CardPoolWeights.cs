using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace AstralShift.HellMaiden.Data
{
	[CreateAssetMenu(fileName = "CardPoolWeights", menuName = "HellMaiden/Data/Cards/Card Pool Weights")]
	public class CardPoolWeights : SerializedScriptableObject
	{
		[Serializable]
		public struct LevelThreshold
		{
			[SerializeField]
			private int level;

			[SerializeField]
			private float mainPoolWeight;

			[SerializeField]
			private float secondaryPoolWeight;

			public int Level => level;

			public float MainPoolWeight => mainPoolWeight;

			public float SecondaryPoolWeight => secondaryPoolWeight;
		}

		[SerializeField]
		private int[] weaponDropLevels;

		[Space]
		[SerializeField]
		private bool weaponsWeightedRandom = true;

		[Header("Level Thresholds / Weights")]
		[SerializeField]
		private bool weaponWeightsInterpolation;

		[SerializeField]
		private LevelThreshold[] _weaponLevelThresholds;

		[SerializeField]
		private float weaponSecondaryPoolWeightIncrement = 0.05f;

		[Space]
		[SerializeField]
		private float weaponReRollWeightReductionFactor = 0.5f;

		[Header("Level Thresholds / Weights")]
		[SerializeField]
		private bool equipmentWeightsInterpolation;

		[SerializeField]
		private LevelThreshold[] _equipmentLevelThresholds;

		[SerializeField]
		private float equipmentSecondaryPoolWeightIncrement = 0.05f;

		[Space]
		[SerializeField]
		private float equipmentReRollWeightReductionFactor = 0.5f;

		[Header("First Slot Roll Weight Bias")]
		[SerializeField]
		private Vector2Int firstSlotEquipmentBiasMinMaxCardCount = new Vector2Int(6, 10);

		[SerializeField]
		private Vector2 firstSlotEquipmentBiasMinMaxChance = new Vector2(0.5f, 1f);

		[Header("Second Slot Roll Weight Bias")]
		[SerializeField]
		private Vector2Int secondSlotEquipmentBiasMinMaxCardCount = new Vector2Int(11, 15);

		[SerializeField]
		private Vector2 secondSlotEquipmentBiasMinMaxChance = new Vector2(0.1f, 5f);

		[Space]
		[SerializeField]
		private bool thirdSlotDefaultDrop;

		public int[] WeaponDropLevels => weaponDropLevels;

		public bool WeaponsWeightedRandom => weaponsWeightedRandom;

		public bool WeaponWeightInterpolation => weaponWeightsInterpolation;

		public LevelThreshold[] WeaponWeights => _weaponLevelThresholds;

		public float WeaponSecondaryPoolWeightIncrement => weaponSecondaryPoolWeightIncrement;

		public float WeaponReRollWeightReductionFactor => equipmentReRollWeightReductionFactor;

		public bool EquipmentWeightInterpolation => equipmentWeightsInterpolation;

		public LevelThreshold[] EquipmentWeights => _equipmentLevelThresholds;

		public float EquipmentSecondaryPoolWeightIncrement => equipmentSecondaryPoolWeightIncrement;

		public float EquipmentReRollWeightReductionFactor => equipmentReRollWeightReductionFactor;

		public Vector2Int FirstSlotEquipmentBiasMinMaxCardCount => firstSlotEquipmentBiasMinMaxCardCount;

		public Vector2 FirstSlotEquipmentBiasMinMaxChance => firstSlotEquipmentBiasMinMaxChance;

		public Vector2Int SecondSlotEquipmentBiasMinMaxCardCount => secondSlotEquipmentBiasMinMaxCardCount;

		public Vector2 SecondSlotEquipmentBiasMinMaxChance => secondSlotEquipmentBiasMinMaxChance;

		public bool IsThirdSlotDefaultDrop => thirdSlotDefaultDrop;
	}
}
