using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AstralShift.HellMaiden.MapGeneration
{
	public class PropReplacer : MonoBehaviour
	{
		public class PropPlacement
		{
			public PropAsset prop;

			public PropAsset ovewrittenProp;

			public PropPlacement(PropAsset prop, PropAsset ovewrittenProp)
			{
				this.prop = prop;
				this.ovewrittenProp = ovewrittenProp;
			}
		}

		public List<PropAsset> propPrefabs;

		[Tooltip("Spawn chance is per tile that its allowed to spawn")]
		[SerializeField]
		public float spawnChance = 0.4f;

		private PropReplacerManager propReplacerManager;

		public virtual void Init(PropReplacerManager propReplacerManager)
		{
			this.propReplacerManager = propReplacerManager;
		}

		public void SpawnInitialProps(int requestId)
		{
			MapGenerator mapGenerator = UnityEngine.Object.FindAnyObjectByType<MapGenerator>();
			if (mapGenerator.Grid == null)
			{
				return;
			}
			int num = mapGenerator.Size.x / 2;
			int num2 = mapGenerator.Size.y / 2;
			List<TileGenerator> list = new List<TileGenerator>();
			Cell[,] grid = mapGenerator.Grid;
			foreach (Cell cell in grid)
			{
				if (cell.x >= num + 2 || cell.x <= num - 2)
				{
					list.Add(mapGenerator.generatedTiles[cell.worldPosition]);
				}
				if (cell.y >= num2 + 2 || cell.y <= num2 - 2)
				{
					list.Add(mapGenerator.generatedTiles[cell.worldPosition]);
				}
			}
			OnMapWrap(list.ToArray(), mapGenerator, requestId);
		}

		public void OnMapWrap(TileGenerator[] allTiles, MapGenerator mapGenerator, int requestId)
		{
			if (propPrefabs == null || propPrefabs.Count == 0)
			{
				return;
			}
			Vector2[] tilePositions = ((IEnumerable<TileGenerator>)allTiles).Select((Func<TileGenerator, Vector2>)((TileGenerator e) => e.transform.position)).ToArray();
			Cell[] array = FindCells(tilePositions, mapGenerator);
			for (int num = 0; num < array.Length; num++)
			{
				if (propReplacerManager.PlacedProps.ContainsKey(requestId) && propReplacerManager.PlacedProps[requestId].ContainsKey(array[num].worldPosition))
				{
					List<PropAsset> ovewrittenProps = propReplacerManager.PlacedProps[requestId][array[num].worldPosition].ovewrittenProps;
					for (int num2 = 0; num2 < ovewrittenProps.Count; num2++)
					{
						ovewrittenProps[num2].gameObject.SetActive(value: false);
					}
					continue;
				}
				bool flag = true;
				foreach (Cell cellNeighbour in mapGenerator.GetCellNeighbours(array[num]))
				{
					if (propReplacerManager.PlacedProps.TryGetValue(requestId, out var value) && value.ContainsKey(cellNeighbour.worldPosition))
					{
						flag = false;
						break;
					}
				}
				if (flag)
				{
					TileGenerator tileGenerator = mapGenerator.generatedTiles[array[num].worldPosition];
					if (tileGenerator.gameObject.activeSelf && tileGenerator.replaceableProps.Count > 0)
					{
						PlaceProp(tileGenerator, requestId);
					}
				}
			}
		}

		public virtual void PlaceProp(TileGenerator tile, int requestId)
		{
			PropAsset randomPropFromList = GetRandomPropFromList();
			PropAsset propToReplace = GetPropToReplace(tile, randomPropFromList, requestId);
			if ((object)propToReplace != null)
			{
				ReplaceProp(tile, randomPropFromList, propToReplace, requestId);
			}
		}

		protected virtual PropAsset GetPropToReplace(TileGenerator tile, PropAsset newProp, int requestId)
		{
			int instanceID = newProp.GetInstanceID();
			if (!tile.replaceablePropsLut.ContainsKey(instanceID))
			{
				tile.FindMatchingSizeReplaceableProps(newProp, cleanEntryOnRepeat: false);
			}
			List<PropAsset> list = tile.replaceablePropsLut[instanceID].ToList();
			if (list.Count == 0)
			{
				return null;
			}
			if (propReplacerManager.PlacedProps.ContainsKey(requestId) && propReplacerManager.PlacedProps[requestId].TryGetValue(tile.transform.position, out var value))
			{
				foreach (PropAsset ovewrittenProp in value.ovewrittenProps)
				{
					list.Remove(ovewrittenProp);
				}
			}
			if (list.Count == 0)
			{
				return null;
			}
			int index = UnityEngine.Random.Range(0, list.Count - 1);
			return list[index];
		}

		protected virtual PropAsset GetRandomPropFromList()
		{
			int index = UnityEngine.Random.Range(0, propPrefabs.Count);
			return propPrefabs[index];
		}

		protected virtual void ReplaceProp(TileGenerator tile, PropAsset newProp, PropAsset propToReplace, int requestId)
		{
			propToReplace.gameObject.SetActive(value: false);
			newProp = UnityEngine.Object.Instantiate(newProp);
			newProp.transform.position = propToReplace.transform.position;
			propReplacerManager.AddOverwrittenProp(requestId, tile.transform.position, newProp, propToReplace);
		}

		private Cell[] FindCells(Vector2[] tilePositions, MapGenerator mapGenerator)
		{
			List<Cell> list = new List<Cell>();
			Cell[,] grid = mapGenerator.Grid;
			foreach (Cell cell in grid)
			{
				foreach (Vector2 vector in tilePositions)
				{
					if (cell.worldPosition == vector)
					{
						list.Add(cell);
					}
				}
			}
			return list.ToArray();
		}
	}
}
