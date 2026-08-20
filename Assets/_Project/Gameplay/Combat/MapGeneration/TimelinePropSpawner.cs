using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AstralShift.HellMaiden.MapGeneration
{
	public class TimelinePropSpawner : MonoBehaviour
	{
		public PropSpawner spawnerPrefab;

		private PropSpawner spawner;

		public int maxAmountToSpawn;

		public int amountSpawned;

		private Transform parent;

		private Dictionary<Vector2, GameObject> placedProps = new Dictionary<Vector2, GameObject>();

		public void Init()
		{
			MapGenerator.OnTilesMoved += OnMapWrap;
			parent = new GameObject("Timeline Prop Spawner Parent").transform;
			spawner = UnityEngine.Object.Instantiate(spawnerPrefab, parent);
			SpawnInitialPots();
		}

		public void ProgressUpdate()
		{
		}

		public void End()
		{
			MapGenerator.OnTilesMoved -= OnMapWrap;
		}

		private void SpawnInitialPots()
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
			OnMapWrap(list.ToArray(), mapGenerator);
		}

		protected void OnMapWrap(TileGenerator[] allTiles, MapGenerator mapGenerator)
		{
			Vector2[] tilePositions = ((IEnumerable<TileGenerator>)allTiles).Select((Func<TileGenerator, Vector2>)((TileGenerator e) => e.transform.position)).ToArray();
			Cell[] array = FindCells(tilePositions, mapGenerator);
			for (int num = 0; num < array.Length; num++)
			{
				if (placedProps.ContainsKey(array[num].worldPosition))
				{
					Debug.Log(placedProps[array[num].worldPosition]);
					continue;
				}
				bool flag = true;
				foreach (Cell cellNeighbour in mapGenerator.GetCellNeighbours(array[num]))
				{
					if (placedProps.ContainsKey(cellNeighbour.worldPosition))
					{
						flag = false;
						break;
					}
				}
				if (flag)
				{
					TileGenerator tileGenerator = mapGenerator.generatedTiles[array[num].worldPosition];
					if (tileGenerator.gameObject.activeSelf)
					{
						PlacePots(tileGenerator);
					}
				}
			}
		}

		private void PlacePots(TileGenerator tile)
		{
			GameObject gameObject = new GameObject("Prop Holder");
			gameObject.transform.parent = parent;
			gameObject.transform.position = tile.transform.position;
			new List<GameObject>();
			placedProps.Add(tile.transform.position, gameObject);
			amountSpawned++;
			_ = amountSpawned;
			_ = maxAmountToSpawn;
			spawner.ClearProps(clearListOnly: true);
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

		private void OnDestroy()
		{
			MapGenerator.OnTilesMoved -= OnMapWrap;
		}
	}
}
