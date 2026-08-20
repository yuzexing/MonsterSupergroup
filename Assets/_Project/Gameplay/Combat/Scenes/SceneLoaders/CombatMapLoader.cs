using System;
using System.Threading;
using AstralShift.HellMaiden.MapGeneration;
using AstralShift.Initialization;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.Scenes.SceneLoaders
{
	public class CombatMapLoader : SceneLoader
	{
		public MapGenerator mapGenerator;

		public SpecialTilesGenerator specialTilesGenerator;

		public override async UniTask LoadAsync()
		{
			_ = 1;
			try
			{
				await specialTilesGenerator.Init();
				await UniTask.RunOnThreadPool((Func<UniTask>)mapGenerator.GenerateAsync, true, default(CancellationToken));
			}
			catch (Exception ex)
			{
				Debug.LogError("ERROR GENERATING MAP: " + ex.ToString());
			}
		}
	}
}
