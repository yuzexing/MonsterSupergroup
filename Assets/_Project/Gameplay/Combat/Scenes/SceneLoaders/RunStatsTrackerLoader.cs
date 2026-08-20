using AstralShift.HellMaiden.GameStats;
using AstralShift.Initialization;
using Cysharp.Threading.Tasks;

namespace AstralShift.HellMaiden.Scenes.SceneLoaders
{
	public class RunStatsTrackerLoader : SceneLoader
	{
		public bool restartTracker = true;

		public override UniTask LoadAsync()
		{
			if (restartTracker)
			{
				RunStatsTracker.Instance?.ResetRunStats();
			}
			RunStatsTracker.Instance?.InitializeRunStats();
			RunStatsTracker.Instance?.LinkGameEvents();
			return UniTask.CompletedTask;
		}

		private void OnDestroy()
		{
			RunStatsTracker.Instance?.UnlinkGameEvents();
		}
	}
}
