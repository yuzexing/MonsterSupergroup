using System;
using System.Collections.Generic;
using System.Linq;
using AstralShift.HellMaiden.Combat;
using UnityEngine;

namespace AstralShift.HellMaiden.MapGeneration
{
	public class PropReplacerManager : SerializedProgressable
	{
		public struct OverwrittenProp
		{
			public int requestId;

			public PropAsset overwrittenProp;

			public OverwrittenProp(int requestId, PropAsset overwrittenProp)
			{
				this.requestId = requestId;
				this.overwrittenProp = overwrittenProp;
			}
		}

		public class ReplacerPropPlacement
		{
			public Dictionary<ReplaceablePropSize, List<PropAsset>> props;

			public List<PropAsset> ovewrittenProps;

			public ReplacerPropPlacement(ReplaceablePropSize propSize, List<PropAsset> prop, List<PropAsset> ovewrittenProp)
			{
				props = new Dictionary<ReplaceablePropSize, List<PropAsset>>();
				props.Add(propSize, new List<PropAsset>());
				ovewrittenProps = ovewrittenProp;
			}

			public void AddReplacedProp(PropAsset newProp, PropAsset replacedProp)
			{
				if (!props.ContainsKey(newProp.propSize))
				{
					props.Add(newProp.propSize, new List<PropAsset>());
				}
				props[newProp.propSize].Add(newProp);
				ovewrittenProps.Add(replacedProp);
			}
		}

		private Dictionary<int, Dictionary<Vector2, ReplacerPropPlacement>> placedProps = new Dictionary<int, Dictionary<Vector2, ReplacerPropPlacement>>();

		private Dictionary<Vector2, List<OverwrittenProp>> overwrittenProps = new Dictionary<Vector2, List<OverwrittenProp>>();

		public PropReplacer _propReplacerPrefab;

		public ShrineSpawner _shrineSpawnerPrefab;

		private PropReplacer _propReplacer;

		private ShrineSpawner _shrineSpawner;

		private List<PropReplacerRequest> propReplacerRequests = new List<PropReplacerRequest>();

		private List<PropReplacerRequest> activePropReplacerRequests = new List<PropReplacerRequest>();

		private int currentRequestIndex;

		public Dictionary<int, Dictionary<Vector2, ReplacerPropPlacement>> PlacedProps => placedProps;

		public void AddPropPlacerRequests(PropReplacerRequest propReplacerInfo)
		{
			propReplacerRequests.Add(propReplacerInfo);
		}

		public void SortPropPlacerRequests()
		{
			propReplacerRequests.Sort((PropReplacerRequest a, PropReplacerRequest b) => a.startTime.CompareTo(b.startTime));
			for (int num = 0; num < propReplacerRequests.Count; num++)
			{
				propReplacerRequests[num].requestId = num;
			}
		}

		public void InitializePropReplacerPrefabs()
		{
			_propReplacer = UnityEngine.Object.Instantiate(_propReplacerPrefab);
			_shrineSpawner = UnityEngine.Object.Instantiate(_shrineSpawnerPrefab);
			_propReplacer.Init(this);
			_shrineSpawner.Init(this);
		}

		public override void Init()
		{
			if (activePropReplacerRequests.Count == 0)
			{
				MapGenerator.OnTilesMoved += OnMapWrap;
			}
			activePropReplacerRequests.Add(propReplacerRequests[currentRequestIndex]);
			PropReplacerRequest propReplacerRequest = propReplacerRequests[currentRequestIndex];
			PropReplacer obj = ((propReplacerRequests[currentRequestIndex] is ShrineReplacerRequest) ? _shrineSpawner : _propReplacer);
			obj.spawnChance = propReplacerRequest.chance;
			obj.propPrefabs = propReplacerRequest.PropAssets;
			obj.SpawnInitialProps(propReplacerRequest.requestId);
			currentRequestIndex++;
		}

		public void OnMapWrap(TileGenerator[] allTiles, MapGenerator mapGenerator)
		{
			if (activePropReplacerRequests.Count == 0)
			{
				return;
			}
			Vector2[] tilePositions = ((IEnumerable<TileGenerator>)allTiles).Select((Func<TileGenerator, Vector2>)((TileGenerator e) => e.transform.position)).ToArray();
			Cell[] array = FindCells(tilePositions, mapGenerator);
			foreach (Cell cell in array)
			{
				List<PropReplacerRequest> list = activePropReplacerRequests.ToList();
				if (overwrittenProps.TryGetValue(cell.worldPosition, out var ovewrittenPropsList))
				{
					int i;
					for (i = 0; i < ovewrittenPropsList.Count; i++)
					{
						ovewrittenPropsList[i].overwrittenProp.gameObject.SetActive(value: false);
						list.RemoveAll((PropReplacerRequest x) => x.requestId == ovewrittenPropsList[i].requestId);
					}
				}
				foreach (Cell cellNeighbour in mapGenerator.GetCellNeighbours(cell))
				{
					foreach (PropReplacerRequest activePropReplacerRequest in activePropReplacerRequests)
					{
						if (placedProps.TryGetValue(activePropReplacerRequest.requestId, out var value) && value.ContainsKey(cellNeighbour.worldPosition))
						{
							list.Remove(activePropReplacerRequest);
						}
					}
				}
				foreach (PropReplacerRequest item in list)
				{
					if (!(UnityEngine.Random.Range(0f, 1f) > item.chance))
					{
						TileGenerator tileGenerator = mapGenerator.generatedTiles[cell.worldPosition];
						PropReplacer propReplacer = ((item is ShrineReplacerRequest) ? _shrineSpawner : _propReplacer);
						propReplacer.propPrefabs = item.PropAssets;
						if (tileGenerator.gameObject.activeSelf && tileGenerator.replaceableProps.Count > 0)
						{
							propReplacer.PlaceProp(tileGenerator, item.requestId);
						}
					}
				}
			}
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

		public override void ProgressUpdate()
		{
		}

		public override void End()
		{
			activePropReplacerRequests.RemoveAll((PropReplacerRequest x) => x.endTime >= ProgressionManager.Instance.CurrentTime);
			if (activePropReplacerRequests.Count == 0)
			{
				MapGenerator.OnTilesMoved -= OnMapWrap;
			}
		}

		public void AddOverwrittenProp(int requestId, Vector2 pos, PropAsset prop, PropAsset overwrittenProp)
		{
			if (!placedProps.ContainsKey(requestId))
			{
				placedProps.Add(requestId, new Dictionary<Vector2, ReplacerPropPlacement>());
			}
			if (!overwrittenProps.ContainsKey(pos))
			{
				overwrittenProps.Add(pos, new List<OverwrittenProp>());
			}
			if (!placedProps[requestId].ContainsKey(pos))
			{
				List<PropAsset> list = new List<PropAsset>();
				List<PropAsset> list2 = new List<PropAsset>();
				list.Add(prop);
				list2.Add(overwrittenProp);
				placedProps[requestId].Add(pos, new ReplacerPropPlacement(list[0].propSize, list, list2));
			}
			placedProps[requestId][pos].AddReplacedProp(prop, overwrittenProp);
			overwrittenProps[pos].Add(new OverwrittenProp(requestId, overwrittenProp));
		}

		public void DisableTileReplaceableProps(Vector2 tilePos)
		{
			foreach (int key in placedProps.Keys)
			{
				if (!placedProps[key].ContainsKey(tilePos))
				{
					continue;
				}
				foreach (List<PropAsset> value in placedProps[key][tilePos].props.Values)
				{
					foreach (PropAsset item in value)
					{
						item.gameObject.SetActive(value: false);
					}
				}
			}
		}
	}
}
