using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.GameStats;
using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.Initialization;
using Cysharp.Threading.Tasks;

namespace AstralShift.HellMaiden.Scenes.SceneLoaders
{
	public class ProgressionLoader : SceneLoader
	{
		public ProgressionManager progressionManagerPrefab;

		public int circleId;

		public override UniTask LoadAsync()
		{
			ProgressionManager componentInChildren = GetComponentInChildren<ProgressionManager>();
			componentInChildren.Init();
			componentInChildren.InitQuests();
			SceneMaster.Instance.OnSceneInit += componentInChildren.StartProgression;
			SceneMaster.Instance.OnSceneInit += Leveler.Instance.ResetLevel;
			RunStatsTracker.Instance.Circle = circleId;
			return UniTask.CompletedTask;
		}
	}
}
