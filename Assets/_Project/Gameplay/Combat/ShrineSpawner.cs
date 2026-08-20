using System.Collections.Generic;
using AstralShift.HellMaiden.Data.Shrines;
using AstralShift.HellMaiden.MapGeneration;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat
{
	public class ShrineSpawner : PropReplacer
	{
		public class ShrineWeight
		{
			public ShrineData shrine;

			public float weight;

			public ShrineWeight(ShrineData data, float weight)
			{
				shrine = data;
				this.weight = weight;
			}
		}

		public class ShrinePlacement
		{
			public GameObject shrine;

			public GameObject ovewrittenObj;

			public ShrinePlacement(GameObject shrine, GameObject obj)
			{
				this.shrine = shrine;
				ovewrittenObj = obj;
			}
		}

		public List<ShrineData> shrines;

		private List<ShrineWeight> weightList;

		private ShrineWeight lastPickedShrine;

		public override void Init(PropReplacerManager propReplacerManager)
		{
			base.Init(propReplacerManager);
			weightList = new List<ShrineWeight>();
			for (int i = 0; i < shrines.Count; i++)
			{
				ShrineWeight item = new ShrineWeight(shrines[i], 1f);
				weightList.Add(item);
			}
		}

		public ShrineData PickShrine()
		{
			float num = 0f;
			foreach (ShrineWeight weight in weightList)
			{
				num += weight.weight;
			}
			float num2 = Random.Range(0f, num);
			bool flag = false;
			ShrineWeight shrineWeight = null;
			foreach (ShrineWeight weight2 in weightList)
			{
				if (num2 <= weight2.weight && !flag)
				{
					shrineWeight = weight2;
					flag = true;
					break;
				}
				weight2.weight += 1f;
				num2 -= weight2.weight;
			}
			if (weightList.Count > 2)
			{
				weightList.Remove(shrineWeight);
				if (lastPickedShrine != null)
				{
					weightList.Add(lastPickedShrine);
				}
				lastPickedShrine = shrineWeight;
			}
			return shrineWeight.shrine;
		}

		public override void PlaceProp(TileGenerator tile, int requestId)
		{
			ShrineData shrineData = PickShrine();
			PropAsset randomPropFromList = GetRandomPropFromList();
			PropAsset propToReplace = GetPropToReplace(tile, randomPropFromList, requestId);
			if (!(propToReplace == null))
			{
				randomPropFromList.GetComponentInChildren<ShrineInteraction>().shrineData = shrineData;
				ReplaceProp(tile, randomPropFromList, propToReplace, requestId);
			}
		}
	}
}
