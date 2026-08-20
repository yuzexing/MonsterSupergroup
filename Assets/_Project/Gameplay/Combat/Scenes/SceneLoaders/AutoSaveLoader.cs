using AstralShift.HellMaiden.Data;
using AstralShift.Initialization;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.Scenes.SceneLoaders
{
	public class AutoSaveLoader : SceneLoader
	{
		[Tooltip("Time interbal in seconds to save from time to time, will not use if 0")]
		public float autoSaveTime;

		public override UniTask LoadAsync()
		{
			return GameDataManager.Instance.SaveGameData().AsUniTask();
		}

		private void Update()
		{
			if ((autoSaveTime != 0f) & (Time.time - GameDataManager.Instance.lastSaveTime > autoSaveTime))
			{
				GameDataManager.Instance.SaveGameData();
			}
		}
	}
}
