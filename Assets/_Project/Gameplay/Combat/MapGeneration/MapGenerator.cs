using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AstralShift.HellMaiden.DevDebug;
using AstralShift.Helpers;
using AstralShift.QTI.Helpers;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.MapGeneration
{
	public class MapGenerator : MonoBehaviour
	{
		[SerializeField]
		private Vector2Int gridSize = Vector2Int.one;

		[SerializeField]
		private float tileSize = 32f;

		[SerializeField]
		private Tile[] tiles;

		private static MapGenerator _instance;

		public Dictionary<Tile, int> tileAmounts = new Dictionary<Tile, int>();

		private Dictionary<(Tile, Tile), int> distanceConstraints = new Dictionary<(Tile, Tile), int>();

		private Vector2Int[] directions = new Vector2Int[4]
		{
			new Vector2Int(-1, 0),
			new Vector2Int(1, 0),
			new Vector2Int(0, -1),
			new Vector2Int(0, 1)
		};

		private Cell[,] grid;

		private List<Vector2Int> availableGridCells;

		public Transform parent;

		[SerializeField]
		private bool compareNeighbors = true;

		[SerializeField]
		private bool applyConstraints = true;

		private bool _constraintsOn;

		[Range(1f, 100f)]
		public int attempts = 5;

		public List<IntDistanceConstraint> distanceConstraintsList = new List<IntDistanceConstraint>();

		public Dictionary<Vector2, TileGenerator> generatedTiles = new Dictionary<Vector2, TileGenerator>();

		private List<TileGenerator> movedTilesList;

		[SerializeField]
		private SpecialTilesGenerator specialTilesGenerator;

		private bool inEditMode;

		public Vector2Int Size => gridSize;

		public float TileSize => tileSize;

		public static float TileSizeStatic
		{
			get
			{
				if (!(_instance != null))
				{
					return (_instance = UnityEngine.Object.FindAnyObjectByType<MapGenerator>()).TileSize;
				}
				return _instance.TileSize;
			}
		}

		public Cell[,] Grid => grid;

		public static event Action OnGenerateEnd;

		public static event Action<TileGenerator[], MapGenerator> OnTilesMoved;

		private void OnDestroy()
		{
			MapGenerator.OnGenerateEnd = null;
			MapGenerator.OnTilesMoved = null;
		}

		public Dictionary<(Tile, Tile), int> GetDistanceConstraints()
		{
			Dictionary<(Tile, Tile), int> dictionary = new Dictionary<(Tile, Tile), int>();
			foreach (IntDistanceConstraint distanceConstraints in distanceConstraintsList)
			{
				(Tile, Tile) key = (tiles[distanceConstraints.key.first], tiles[distanceConstraints.key.second]);
				if (dictionary.ContainsKey(key))
				{
					Debug.LogWarning("Map Generation Constraints: Repeated tile pairs, will replace value with last pairing in the list!");
				}
				dictionary[key] = distanceConstraints.distance;
				(Tile, Tile) key2 = (tiles[distanceConstraints.key.second], tiles[distanceConstraints.key.first]);
				dictionary[key2] = distanceConstraints.distance;
			}
			return dictionary;
		}

		public async UniTask GenerateAsync()
		{
			await UniTask.SwitchToMainThread();
			DeleteAll();
			if (DeveloperDebug.PlayWithoutMapGeneration)
			{
				return;
			}
			inEditMode = !Application.isPlaying;
			await UniTask.SwitchToThreadPool();
			_constraintsOn = applyConstraints;
			bool flag = false;
			for (int i = 0; i < attempts; i++)
			{
				flag = await TryGenerateGridAsync();
				if (flag)
				{
					break;
				}
			}
			if (!flag && _constraintsOn)
			{
				Debug.LogError("MapGenerator: Failed to generate grid, removing constraints!");
				_constraintsOn = false;
				for (int i = 0; i < attempts; i++)
				{
					flag = await TryGenerateGridAsync();
					if (flag)
					{
						break;
					}
				}
			}
			if (!flag)
			{
				Debug.LogError("MapGenerator: Fatal! Impossible to generate grid.");
				return;
			}
			Debug.Log("MapGenerator: Grid generated successfully!");
			Debug.Log("MapGenerator: Spawning tiles...");
			generatedTiles = new Dictionary<Vector2, TileGenerator>();
			movedTilesList = new List<TileGenerator>();
			if (true)
			{
				Cell[,] array = grid;
				foreach (Cell cell in array)
				{
					Dictionary<Vector2, TileGenerator> dictionary = generatedTiles;
					Vector2 worldPosition = cell.worldPosition;
					dictionary[worldPosition] = await SpawnTileAsync(cell.tile, cell.worldPosition);
				}
			}
			else
			{
				List<UniTask<TileGenerator>> list = new List<UniTask<TileGenerator>>();
				Cell[,] array2 = grid;
				foreach (Cell cell2 in array2)
				{
					generatedTiles[cell2.worldPosition] = null;
					list.Add(SpawnTileAsync(cell2.tile, cell2.worldPosition));
				}
				TileGenerator[] tiles = await UniTask.WhenAll(list);
				await UniTask.SwitchToMainThread();
				foreach (Vector2 worldPos in generatedTiles.Keys.ToList())
				{
					generatedTiles[worldPos] = tiles.FirstOrDefault((TileGenerator tg) => tg.transform.position.To2D() == worldPos);
				}
				await UniTask.SwitchToThreadPool();
			}
			Debug.Log("MapGenerator: Tiles spawning completed successfully!");
			await UniTask.SwitchToMainThread();
			if (!inEditMode)
			{
				StartCoroutine(VerifyMapWrapping(1f));
			}
		}

		private async UniTask<bool> TryGenerateGridAsync()
		{
			grid = new Cell[gridSize.x, gridSize.y];
			availableGridCells = new List<Vector2Int>();
			distanceConstraints = GetDistanceConstraints();
			MonoBehaviour.print("MapGenerator: Creating Grid.");
			float startPositionX = (0f - (float)gridSize.x) / 2f * TileSize;
			float startPositionY = (0f - (float)gridSize.y) / 2f * TileSize;
			for (int i = 0; i < gridSize.x; i++)
			{
				for (int j = 0; j < gridSize.y; j++)
				{
					grid[i, j] = new Cell(new Vector2(startPositionX + tileSize * (float)i, startPositionY + tileSize * (float)j), i, j);
					availableGridCells.Add(new Vector2Int(i, j));
				}
			}
			float totalWeight = 0f;
			tileAmounts = new Dictionary<Tile, int>();
			Tile[] array = tiles;
			foreach (Tile tile in array)
			{
				if (tile.specificPosition)
				{
					if (tile.position.x > 0 && tile.position.x < gridSize.x && tile.position.y > 0 && tile.position.y < gridSize.y)
					{
						tile.minAmount = 0;
						tile.maxAmount = 0;
						if (!inEditMode)
						{
							Vector2 position = new Vector2(startPositionX + TileSize * (float)tile.position.x, startPositionY + TileSize * (float)tile.position.y);
							await specialTilesGenerator.LoadTileAsync(tile, position);
							grid[tile.position.x, tile.position.y].locked = true;
						}
						else
						{
							grid[tile.position.x, tile.position.y].tile = tile;
							availableGridCells.Remove(tile.position);
						}
					}
					else
					{
						Debug.LogError("MapGenerator: Specific position is out of bounds!!");
					}
				}
				else if (tile.minAmount != 0)
				{
					totalWeight += tile.weight;
					tileAmounts.Add(tile, tile.minAmount);
				}
			}
			if (tileAmounts.Count == 0)
			{
				Tile[] array2 = tiles;
				foreach (Tile tile2 in array2)
				{
					if (!tile2.specificPosition)
					{
						totalWeight += tile2.weight;
						tileAmounts.Add(tile2, tile2.maxAmount);
					}
				}
			}
			int num = availableGridCells.ToArray().Length;
			for (int m = 0; m < num; m++)
			{
				Tile tile3 = SelectTileByWeight(totalWeight);
				int num2 = ((tile3.minAmount <= 0) ? 1 : tile3.minAmount);
				tileAmounts[tile3] -= num2;
				if (tileAmounts[tile3] <= 0)
				{
					totalWeight = RemoveFromPool(totalWeight, tile3);
					if (tileAmounts.Count == 0)
					{
						Tile[] array2 = tiles;
						foreach (Tile tile4 in array2)
						{
							if (tile4.maxAmount - tile4.minAmount > 0 && !tile4.specificPosition)
							{
								totalWeight += tile4.weight;
								tileAmounts.Add(tile4, tile4.maxAmount - tile4.minAmount);
							}
						}
					}
				}
				if (num2 > availableGridCells.Count)
				{
					Debug.LogError("MapGenerator: Not enough cells for the amount of required tiles! Change gridSize or Remove minAmount of tiles!");
					return false;
				}
				List<Vector2Int> list = new List<Vector2Int>(availableGridCells);
				for (int n = 0; n < num2; n++)
				{
					if (list.Count == 0)
					{
						totalWeight = RemoveFromPool(totalWeight, tile3);
						m--;
						if (tileAmounts.Count != 0)
						{
							break;
						}
						Debug.LogError("MapGenerator: Failed at placing tile!");
						return false;
					}
					Vector2Int vector2Int = list[RandomHelpers.GetRandomInt(0, list.Count)];
					if (CheckConstraints(tile3, vector2Int))
					{
						availableGridCells.Remove(vector2Int);
						grid[vector2Int.x, vector2Int.y].tile = tile3;
					}
					else
					{
						list.Remove(vector2Int);
						n--;
					}
				}
			}
			return true;
		}

		public async UniTask<TileGenerator> SpawnTileAsync(Tile tile, Vector2 spawnPosition)
		{
			await UniTask.SwitchToMainThread();
			Debug.Log("Spawning Tile " + tile.prefab.name);
			TileGenerator newTile = UnityEngine.Object.Instantiate(tile.prefab, spawnPosition, Quaternion.identity, parent);
			if (tile.setPiece != null)
			{
				PropSpawner setPieceSpawner = UnityEngine.Object.Instantiate(tile.setPiece, spawnPosition, Quaternion.identity, newTile.transform);
				await UniTask.RunOnThreadPool(() => setPieceSpawner.SpawnPropsAsync(newTile.area, newTile.parent, null));
				newTile.name = tile.prefab.name + tile.setPiece.name;
			}
			else
			{
				newTile.name = tile.prefab.name;
			}
			await UniTask.SwitchToThreadPool();
			await newTile.GenerateAsync();
			return newTile;
		}

		public async UniTask<TileGenerator> SpawnTileAsync(TileGenerator tile, Vector2 spawnPosition)
		{
			await UniTask.SwitchToMainThread();
			Debug.Log("Spawning Tile " + tile.name);
			TileGenerator newTile = UnityEngine.Object.Instantiate(tile, spawnPosition, Quaternion.identity, parent);
			newTile.name = tile.name;
			await UniTask.SwitchToThreadPool();
			await newTile.GenerateAsync();
			return newTile;
		}

		private bool CompareNeighbors(Tile tile, Vector2Int position)
		{
			for (int i = 0; i < directions.Length; i++)
			{
				if (position.x + directions[i].x <= gridSize.x && position.x + directions[i].x >= 0 && position.y + directions[i].y <= gridSize.y && position.y + directions[i].y >= 0)
				{
					Vector2Int vector2Int = position + directions[i];
					vector2Int.x = Mathf.Clamp(vector2Int.x, 0, gridSize.x - 1);
					vector2Int.y = Mathf.Clamp(vector2Int.y, 0, gridSize.y - 1);
					Tile tile2 = grid[vector2Int.x, vector2Int.y].tile;
					if (tile2 != null && tile2.Equals(tile))
					{
						return false;
					}
				}
			}
			return true;
		}

		private bool VerifyDistanceConstraints(Tile tile, Vector2Int position)
		{
			for (int i = 0; i < tiles.Length; i++)
			{
				(Tile, Tile) key = (tile, tiles[i]);
				if (!distanceConstraints.ContainsKey(key))
				{
					continue;
				}
				for (int j = 0; j < gridSize.x; j++)
				{
					for (int k = 0; k < gridSize.y; k++)
					{
						if (grid[j, k].tile == key.Item2 && (float)distanceConstraints[key] >= Vector2Int.Distance(new Vector2Int(j, k), position))
						{
							return false;
						}
					}
				}
			}
			return true;
		}

		private bool CheckConstraints(Tile tile, Vector2Int position)
		{
			if ((compareNeighbors && CompareNeighbors(tile, position)) || !compareNeighbors)
			{
				if (!_constraintsOn || !VerifyDistanceConstraints(tile, position))
				{
					return !_constraintsOn;
				}
				return true;
			}
			return false;
		}

		private float RemoveFromPool(float totalWeight, Tile tile)
		{
			tileAmounts.Remove(tile);
			totalWeight -= tile.weight;
			return totalWeight;
		}

		private Tile SelectTileByWeight(float totalWeight)
		{
			float randomFloat = RandomHelpers.GetRandomFloat(0f, totalWeight);
			float num = 0f;
			foreach (Tile key in tileAmounts.Keys)
			{
				num += key.weight;
				if (randomFloat <= num)
				{
					return key;
				}
			}
			return null;
		}

		public void DeleteAll()
		{
			if (parent == null)
			{
				Debug.LogError("Parent object is not assigned!");
				return;
			}
			Transform[] allChildren = GetAllChildren(parent);
			if (allChildren.Length != 0)
			{
				for (int num = allChildren.Length - 1; num >= 0; num--)
				{
					UnityEngine.Object.Destroy(allChildren[num].gameObject);
				}
			}
		}

		public static Transform[] GetAllChildren(Transform parent)
		{
			Transform[] array = new Transform[parent.childCount];
			for (int i = 0; i < parent.childCount; i++)
			{
				array[i] = parent.GetChild(i);
			}
			return array;
		}

		public TileGenerator GetRandomSpawnedTileByDistance(Vector2Int position, uint distance, bool distanceCanBeGreater)
		{
			List<Cell> list = new List<Cell>();
			for (int i = 0; i < gridSize.x; i++)
			{
				for (int j = 0; j < gridSize.y; j++)
				{
					int num = System.Math.Abs(i - position.x) + System.Math.Abs(j - position.y);
					if (!grid[i, j].locked && ((distanceCanBeGreater && num >= distance) || (!distanceCanBeGreater && num == distance)))
					{
						list.Add(grid[i, j]);
					}
				}
			}
			if (list.Count > 0)
			{
				Cell cell = list[RandomHelpers.GetRandomInt(0, list.Count)];
				cell.locked = true;
				return generatedTiles[cell.worldPosition];
			}
			return null;
		}

		public Vector2Int GetRandomTilePositionByDistance(Vector2Int position, uint distance, bool distanceCanBeGreater)
		{
			float angleDeg = UnityEngine.Random.Range(0, 360);
			Vector2 vector = RotateVector2(Vector2.right, angleDeg);
			Vector2 vector2 = position + vector.normalized * distance * TileSize;
			return new Vector2Int((int)vector2.x / (int)tileSize * (int)tileSize, (int)vector2.y / (int)tileSize * (int)tileSize);
		}

		private Vector2 RotateVector2(Vector2 v, float angleDeg)
		{
			angleDeg *= MathF.PI / 180f;
			return new Vector2(v.x * Mathf.Cos(angleDeg) - v.y * Mathf.Sin(angleDeg), v.x * Mathf.Sin(angleDeg) + v.y * Mathf.Cos(angleDeg));
		}

		public static float GetDistanceToPlayerInTiles(Vector2 objectPosition)
		{
			Vector2 b = GameDirector.Instance.Player.CurrentPosition;
			return Vector2.Distance(objectPosition, b) / TileSizeStatic;
		}

		public TileGenerator GetTileByPosition(Vector2 position)
		{
			return generatedTiles[position];
		}

		public TileGenerator GetPlayerTile()
		{
			Vector2 vector = GameDirector.Instance.Player.CurrentPosition;
			float num = TileSize / 2f;
			float num2 = ((vector.x >= 0f) ? ((float)Mathf.FloorToInt(vector.x / TileSize) * TileSize) : ((float)Mathf.CeilToInt(vector.x / TileSize) * TileSize));
			num2 -= num;
			float num3 = ((vector.y >= 0f) ? ((float)Mathf.FloorToInt(vector.y / TileSize) * TileSize) : ((float)Mathf.CeilToInt(vector.y / TileSize) * TileSize));
			num3 -= num;
			Vector2 key = new Vector2(num2, num3);
			return generatedTiles[key];
		}

		public TileGenerator GetCoordinatesFromTile(Vector2 pos)
		{
			Vector2 worldPosition = grid[0, 0].worldPosition;
			int value = Mathf.FloorToInt((pos.x - worldPosition.x + TileSize * 0.1f) / TileSize);
			int value2 = Mathf.FloorToInt((pos.y - worldPosition.y + TileSize * 0.1f) / TileSize);
			value = Mathf.Clamp(value, 0, gridSize.x - 1);
			value2 = Mathf.Clamp(value2, 0, gridSize.y - 1);
			Vector2 worldPosition2 = grid[value, value2].worldPosition;
			if (generatedTiles.TryGetValue(worldPosition2, out var value3))
			{
				return value3;
			}
			return GetClosestTile(pos);
		}

		private TileGenerator GetClosestTile(Vector2 position)
		{
			TileGenerator result = null;
			float num = float.MaxValue;
			foreach (KeyValuePair<Vector2, TileGenerator> generatedTile in generatedTiles)
			{
				float sqrMagnitude = (generatedTile.Key - position).sqrMagnitude;
				if (sqrMagnitude < num)
				{
					num = sqrMagnitude;
					result = generatedTile.Value;
				}
			}
			return result;
		}

		private IEnumerator VerifyMapWrapping(float timestep)
		{
			WaitForSeconds waitYield = new WaitForSeconds(timestep);
			while (true)
			{
				yield return waitYield;
				Vector2 vector = GameDirector.Instance.Player.CurrentPosition;
				movedTilesList.Clear();
				bool flag = false;
				if (vector.x > grid[gridSize.x / 2, 0].worldPosition.x + TileSize)
				{
					for (int i = 0; i < gridSize.y; i++)
					{
						Cell cell = grid[0, i];
						Vector2 worldPosition = cell.worldPosition;
						Vector2 vector2 = grid[gridSize.x - 1, i].worldPosition + new Vector2(TileSize, 0f);
						TileGenerator tileGenerator = generatedTiles[worldPosition];
						generatedTiles.Remove(worldPosition);
						tileGenerator.transform.position = vector2;
						generatedTiles.Add(vector2, tileGenerator);
						movedTilesList.Add(tileGenerator);
						for (int j = 0; j < gridSize.x - 1; j++)
						{
							int num = j;
							int num2 = j + 1;
							grid[num, i] = grid[num2, i];
							grid[num, i].x = num;
						}
						cell.x = gridSize.x - 1;
						grid[gridSize.x - 1, i] = cell;
						grid[gridSize.x - 1, i].worldPosition = vector2;
					}
					flag = true;
				}
				else if (vector.x < grid[gridSize.x / 2, 0].worldPosition.x)
				{
					for (int k = 0; k < gridSize.y; k++)
					{
						Cell cell2 = grid[gridSize.x - 1, k];
						Vector2 worldPosition2 = cell2.worldPosition;
						Vector2 vector3 = grid[0, k].worldPosition - new Vector2(TileSize, 0f);
						TileGenerator tileGenerator2 = generatedTiles[worldPosition2];
						generatedTiles.Remove(worldPosition2);
						tileGenerator2.transform.position = vector3;
						generatedTiles.Add(vector3, tileGenerator2);
						movedTilesList.Add(tileGenerator2);
						for (int num3 = gridSize.x - 1; num3 >= 1; num3--)
						{
							int num4 = num3;
							int num5 = num3 - 1;
							grid[num4, k] = grid[num5, k];
							grid[num4, k].x = num4;
						}
						cell2.x = 0;
						grid[0, k] = cell2;
						grid[0, k].worldPosition = vector3;
					}
					flag = true;
				}
				if (vector.y > grid[0, gridSize.y / 2].worldPosition.y + TileSize)
				{
					for (int l = 0; l < gridSize.x; l++)
					{
						Cell cell3 = grid[l, 0];
						Vector2 worldPosition3 = cell3.worldPosition;
						Vector2 vector4 = grid[l, gridSize.y - 1].worldPosition + new Vector2(0f, TileSize);
						TileGenerator tileGenerator3 = generatedTiles[worldPosition3];
						generatedTiles.Remove(worldPosition3);
						tileGenerator3.transform.position = vector4;
						generatedTiles.Add(vector4, tileGenerator3);
						movedTilesList.Add(tileGenerator3);
						for (int m = 0; m < gridSize.y - 1; m++)
						{
							int num6 = m;
							int num7 = m + 1;
							grid[l, num6] = grid[l, num7];
							grid[l, num6].y = num6;
						}
						cell3.y = gridSize.y - 1;
						grid[l, gridSize.y - 1] = cell3;
						grid[l, gridSize.y - 1].worldPosition = vector4;
					}
					flag = true;
				}
				else if (vector.y < grid[0, gridSize.y / 2].worldPosition.y)
				{
					for (int n = 0; n < gridSize.x; n++)
					{
						Cell cell4 = grid[n, gridSize.y - 1];
						Vector2 worldPosition4 = cell4.worldPosition;
						Vector2 vector5 = grid[n, 0].worldPosition - new Vector2(0f, TileSize);
						TileGenerator tileGenerator4 = generatedTiles[worldPosition4];
						generatedTiles.Remove(worldPosition4);
						tileGenerator4.transform.position = vector5;
						generatedTiles.Add(vector5, tileGenerator4);
						movedTilesList.Add(tileGenerator4);
						for (int num8 = gridSize.y - 1; num8 >= 1; num8--)
						{
							int num9 = num8;
							int num10 = num8 - 1;
							grid[n, num9] = grid[n, num10];
							grid[n, num9].y = num9;
						}
						cell4.y = 0;
						grid[n, 0] = cell4;
						grid[n, 0].worldPosition = vector5;
					}
					flag = true;
				}
				if (!flag)
				{
					continue;
				}
				for (int num11 = 0; num11 < movedTilesList.Count; num11++)
				{
					for (int num12 = 0; num12 < movedTilesList[num11].replaceableProps.Count; num12++)
					{
						movedTilesList[num11].replaceableProps[num12].gameObject.SetActive(value: true);
					}
				}
				specialTilesGenerator.IgnoreGeneratedSpecialTilesSlots(generatedTiles.Values.ToArray());
				MapGenerator.OnTilesMoved?.Invoke(movedTilesList.ToArray(), this);
			}
		}

		public List<Cell> GetCellNeighbours(Cell cell)
		{
			List<Cell> list = new List<Cell>();
			if (cell.x > 0)
			{
				list.Add(grid[cell.x - 1, cell.y]);
			}
			if (cell.x < gridSize.x - 1)
			{
				list.Add(grid[cell.x + 1, cell.y]);
			}
			if (cell.y > 0)
			{
				list.Add(grid[cell.x, cell.y - 1]);
			}
			if (cell.y < gridSize.y - 1)
			{
				list.Add(grid[cell.x, cell.y + 1]);
			}
			return list;
		}
	}
}
