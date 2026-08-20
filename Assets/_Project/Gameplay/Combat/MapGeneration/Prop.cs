using System;
using System.Collections.Generic;
using UnityEngine;

namespace AstralShift.HellMaiden.MapGeneration
{
	[Serializable]
	public class Prop
	{
		public class PrefabSettings
		{
			public int idx;

			public Vector2 dimensions;

			public Vector2 Dimensions => dimensions;

			public int Idx
			{
				get
				{
					return idx;
				}
				set
				{
					idx = value;
				}
			}

			public PrefabSettings(int idx, Vector2 dimensions)
			{
				this.idx = idx;
				this.dimensions = dimensions;
			}
		}

		public PropAsset prefab;

		public bool isPrefabList;

		public List<PropAsset> prefabs;

		public float weight;

		public int minAmount;

		public int maxAmount;

		[Tooltip("Optional flip chance for X and Y axes (0 = no flip, 1 = always flip, 0.5 = 50%)")]
		[Range(0f, 1f)]
		public float flipChanceX;

		[Tooltip("Optional flip chance for X and Y axes (0 = no flip, 1 = always flip, 0.5 = 50%)")]
		[Range(0f, 1f)]
		public float flipChanceY;

		[Tooltip("If true, object can't spawn on top of the layers specified in CollisionLayerMask in GridSettings")]
		public bool excludeLayers;

		private int _counter;

		public List<int> PrefabsToSpawnIdxList = new List<int>();

		public float Width { get; set; }

		public float Height { get; set; }

		public Vector2[] PrefabsDimensions { get; set; }

		public PropAsset GetPrefab()
		{
			if (!isPrefabList)
			{
				return prefab;
			}
			return prefabs[PrefabsToSpawnIdxList[_counter++]];
		}

		public void ResetCounter()
		{
			_counter = 0;
		}
	}
}
