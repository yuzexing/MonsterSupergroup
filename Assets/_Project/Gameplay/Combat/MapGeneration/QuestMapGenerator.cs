using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Quests;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.MapGeneration
{
	[RequireComponent(typeof(MapGenerator))]
	public class QuestMapGenerator : MonoBehaviour
	{
		[SerializeField]
		private MapGenerator mapGenerator;

		[SerializeField]
		private SpecialTilesGenerator specialTilesGenerator;

		[SerializeField]
		private TransitionInMap transitionInMap;

		public Dictionary<DivinaQuestGoal, TileGenerator> generatedTiles;

		public Dictionary<TileGenerator, TileGenerator> questTilesReplacedTiles;

		public static Action<TileGenerator, TileGenerator> OnTilesSwapped;

		public static Action OnGenerateEnd;

		public static QuestMapGenerator Instance { get; private set; }

		private void Awake()
		{
			if (Instance == null)
			{
				Instance = this;
			}
			else
			{
				UnityEngine.Object.Destroy(this);
			}
		}

		public async UniTask GenerateTiles()
		{
			await UniTask.SwitchToMainThread();
			generatedTiles = new Dictionary<DivinaQuestGoal, TileGenerator>();
			questTilesReplacedTiles = new Dictionary<TileGenerator, TileGenerator>();
			foreach (DivinaQuestGoal quest in ProgressionManager.Instance.Quests)
			{
				if (!quest.IsQuestValid())
				{
					continue;
				}
				foreach (DivinaQuestGoal item in (from qg in quest.GetAllQuestGoals()
					where qg.hasSpecificTile
					select qg).ToList())
				{
					if (!item.reUseTile)
					{
						generatedTiles[item] = item.tile;
					}
				}
			}
			await UniTask.RunOnThreadPool((Func<UniTask>)LoadQuestTiles, true, default(CancellationToken));
			await UniTask.DelayFrame(1);
			OnGenerateEnd?.Invoke();
			OnGenerateEnd = null;
		}

		public async UniTask LoadQuestTiles()
		{
			Vector2 tilePosition = new Vector2(-10000f, mapGenerator.TileSize * (float)generatedTiles.Count);
			for (int i = 0; i < generatedTiles.Count; i++)
			{
				DivinaQuestGoal key = generatedTiles.ElementAt(i).Key;
				Dictionary<DivinaQuestGoal, TileGenerator> dictionary = generatedTiles;
				DivinaQuestGoal key2 = key;
				dictionary[key2] = await mapGenerator.SpawnTileAsync(generatedTiles[key], tilePosition);
				tilePosition.y -= mapGenerator.TileSize;
			}
		}

		public TileGenerator ActivateQuestTile(DivinaQuestGoal questGoal)
		{
			if (mapGenerator.Grid == null)
			{
				Debug.LogWarning("Map generator has not been initialized.");
				return null;
			}
			uint distanceToPlayer = questGoal.distanceToPlayer;
			int num = (int)Math.Floor((float)mapGenerator.Size.x / 2f);
			Vector2Int position = new Vector2Int(num, num);
			TileGenerator randomSpawnedTileByDistance = mapGenerator.GetRandomSpawnedTileByDistance(position, distanceToPlayer, questGoal.greaterOrEquals);
			DivinaQuestGoal divinaQuestGoal = generatedTiles.Keys.First((DivinaQuestGoal gt) => gt.name == questGoal.name);
			TileGenerator tileGenerator = generatedTiles[divinaQuestGoal];
			if (randomSpawnedTileByDistance != null)
			{
				tileGenerator.transform.position = randomSpawnedTileByDistance.transform.position;
				randomSpawnedTileByDistance.gameObject.SetActive(value: false);
				questTilesReplacedTiles[tileGenerator] = randomSpawnedTileByDistance;
				OnTilesSwapped?.Invoke(tileGenerator, randomSpawnedTileByDistance);
				ProgressionManager.Instance.MainProgressionTimeline.PropReplacerManagerInstance.DisableTileReplaceableProps(randomSpawnedTileByDistance.transform.position);
			}
			else
			{
				Vector2Int randomTilePositionByDistance = mapGenerator.GetRandomTilePositionByDistance(position, distanceToPlayer, questGoal.greaterOrEquals);
				Vector2 vector = (Vector2)mapGenerator.GetPlayerTile().transform.position + (Vector2)randomTilePositionByDistance;
				tileGenerator.transform.position = vector;
			}
			specialTilesGenerator.AddSpecialTile(tileGenerator);
			if (transitionInMap != null && divinaQuestGoal.transitionsAutomatically)
			{
				transitionInMap.TransitionToPosition((Vector2)tileGenerator.transform.position + new Vector2(mapGenerator.TileSize * 0.5f, mapGenerator.TileSize * 0.5f));
			}
			return tileGenerator;
		}

		public void DisableQuestTile(DivinaQuestGoal quest)
		{
			TileGenerator tileGenerator = generatedTiles[quest];
			if (questTilesReplacedTiles.TryGetValue(tileGenerator, out var value))
			{
				value.gameObject.SetActive(value: true);
			}
			tileGenerator.gameObject.SetActive(value: false);
			specialTilesGenerator.RemoveSpecialTile(tileGenerator);
			OnTilesSwapped?.Invoke(value, tileGenerator);
		}

		public TileGenerator FindSpawnedTile(string tileName)
		{
			return mapGenerator.parent.Find(tileName).GetComponent<TileGenerator>();
		}

		private void OnDestroy()
		{
			OnGenerateEnd = null;
			OnTilesSwapped = null;
		}
	}
}
