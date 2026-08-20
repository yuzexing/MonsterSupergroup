using System;
using AstralShift.HellMaiden.MapGeneration;
using AstralShift.Initialization;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.Scenes.SceneLoaders
{
	public class QuestLoader : SceneLoader
	{
		public override async UniTask LoadAsync()
		{
			try
			{
				await QuestMapGenerator.Instance.GenerateTiles();
			}
			catch (Exception ex)
			{
				Debug.LogError("ERROR GENERATING QUEST: " + ex.ToString());
			}
		}
	}
}
