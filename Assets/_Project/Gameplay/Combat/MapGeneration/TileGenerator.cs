using System.Collections.Generic;
using AstralShift.QTI.Helpers.Attributes;
using Cysharp.Threading.Tasks;
using Pathfinding;
using UnityEngine;

namespace AstralShift.HellMaiden.MapGeneration
{
	public class TileGenerator : MonoBehaviour
	{
		[SerializeField]
		private List<PropSpawner> spawners = new List<PropSpawner>();

		public Transform parent;

		public BoxCollider2D area;

		[SerializeField]
		private bool timelapse;

		[ConditionalHide("timelapse", true)]
		[SerializeField]
		[Range(1f, 2000f)]
		private int timestep = 800;

		[HideInInspector]
		public List<PropAsset> replaceableProps;

		[HideInInspector]
		public Dictionary<int, List<PropAsset>> replaceablePropsLut;

		public GridGraph GridGraph { get; set; }

		public async UniTask GenerateAsync()
		{
			replaceableProps = new List<PropAsset>();
			replaceablePropsLut = new Dictionary<int, List<PropAsset>>();
			foreach (PropSpawner spawner in spawners)
			{
				spawner.ClearProps();
			}
			if (timelapse)
			{
				return;
			}
			foreach (PropSpawner spawner2 in spawners)
			{
				await spawner2.SpawnPropsAsync(area, parent, replaceableProps);
			}
		}

		public void DeleteAll()
		{
			if (parent == null)
			{
				Debug.LogError("Parent object is not assigned!");
				return;
			}
			Transform[] allChildren = MapGenerator.GetAllChildren(parent);
			for (int num = allChildren.Length - 1; num >= 0; num--)
			{
				Object.Destroy(allChildren[num].gameObject);
			}
		}

		public Vector3 GetCenter()
		{
			return base.transform.position + new Vector3(area.offset.x, area.offset.y, 0f);
		}

		public void FindMatchingSizeReplaceableProps(PropAsset propToSort, bool cleanEntryOnRepeat)
		{
			if (!replaceablePropsLut.ContainsKey(propToSort.GetInstanceID()))
			{
				replaceablePropsLut.Add(propToSort.GetInstanceID(), new List<PropAsset>());
			}
			else if (cleanEntryOnRepeat)
			{
				replaceablePropsLut[propToSort.GetInstanceID()].Clear();
			}
			PropAsset propAsset = Object.Instantiate(propToSort, Vector3.zero, Quaternion.identity);
			foreach (PropAsset replaceableProp in replaceableProps)
			{
				if (propAsset.propSize == replaceableProp.propSize)
				{
					Bounds bounds = replaceableProp.GetBounds();
					Bounds bounds2 = propAsset.GetBounds();
					if (!(bounds.min.x - bounds.center.x >= bounds2.min.x) && !(bounds.max.x - bounds.center.x <= bounds2.max.x) && !(bounds.min.y - bounds.center.y >= bounds2.min.y) && !(bounds.max.y - bounds.center.y <= bounds2.max.y))
					{
						replaceablePropsLut[propToSort.GetInstanceID()].Add(replaceableProp);
					}
				}
			}
			Object.Destroy(propAsset.gameObject);
		}
	}
}
