using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.DevDebug;
using AstralShift.HellMaiden.Scenes;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.MapGeneration
{
	[RequireComponent(typeof(MapGenerator))]
	public class SpecialTilesGenerator : MonoBehaviour
	{
		public MapGenerator mapGenerator;

		public Dictionary<Vector2, TileGenerator> initialTiles;

		private List<TileGenerator> generatedSpecialTiles;

		public static Action<TileGenerator, TileGenerator> OnTilesSwapped;

		public UniTask Init()
		{
			if (DeveloperDebug.PlayWithoutMapGeneration)
			{
				return UniTask.CompletedTask;
			}
			initialTiles = new Dictionary<Vector2, TileGenerator>();
			generatedSpecialTiles = new List<TileGenerator>();
			mapGenerator = ((mapGenerator != null) ? mapGenerator : GetComponent<MapGenerator>());
			SceneMaster.Instance.OnSceneInit += ActivateInitialTiles;
			return UniTask.CompletedTask;
		}

		public async UniTask LoadTileAsync(Tile specialTile, Vector2 position)
		{
			Vector2 spawnPosition = new Vector2(-10000f + mapGenerator.TileSize, mapGenerator.TileSize * (float)generatedSpecialTiles.Count);
			Dictionary<Vector2, TileGenerator> dictionary = initialTiles;
			dictionary[position] = await mapGenerator.SpawnTileAsync(specialTile, spawnPosition);
		}

		public void ActivateInitialTiles()
		{
			foreach (KeyValuePair<Vector2, TileGenerator> initialTile in initialTiles)
			{
				TileGenerator tileByPosition = mapGenerator.GetTileByPosition(initialTile.Key);
				initialTile.Value.transform.position = tileByPosition.transform.position;
				tileByPosition.gameObject.SetActive(value: false);
				OnTilesSwapped?.Invoke(initialTile.Value, tileByPosition);
				AddSpecialTile(initialTile.Value);
			}
		}

		public void AddSpecialTile(TileGenerator tileGenerator)
		{
			generatedSpecialTiles.Add(tileGenerator);
		}

		public void RemoveSpecialTile(TileGenerator tileGenerator)
		{
			generatedSpecialTiles.Remove(tileGenerator);
		}

		public void IgnoreGeneratedSpecialTilesSlots(TileGenerator[] gts)
		{
			foreach (TileGenerator tileGenerator in gts)
			{
				tileGenerator.gameObject.SetActive(value: true);
				foreach (TileGenerator generatedSpecialTile in generatedSpecialTiles)
				{
					if (tileGenerator.transform.position == generatedSpecialTile.transform.position)
					{
						tileGenerator.gameObject.SetActive(value: false);
						if (!QuestMapGenerator.Instance.questTilesReplacedTiles.ContainsKey(generatedSpecialTile))
						{
							QuestMapGenerator.Instance.questTilesReplacedTiles[generatedSpecialTile] = tileGenerator;
						}
					}
				}
			}
		}
	}
}
