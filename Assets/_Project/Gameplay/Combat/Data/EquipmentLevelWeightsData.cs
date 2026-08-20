using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace AstralShift.HellMaiden.Data
{
	[CreateAssetMenu(fileName = "EquipmentLevelWeights", menuName = "HellMaiden/Data/Cards/Equipment Level Weights")]
	public class EquipmentLevelWeightsData : SerializedScriptableObject
	{
		[Serializable]
		public struct LevelThreshold
		{
			[SerializeField]
			private int level;

			[SerializeField]
			private float[] levelWeight;

			public int Level => level;

			public float[] LevelWeight => levelWeight;
		}

		[SerializeField]
		private LevelThreshold[] weights;

		public LevelThreshold[] Weights => weights;
	}
}
