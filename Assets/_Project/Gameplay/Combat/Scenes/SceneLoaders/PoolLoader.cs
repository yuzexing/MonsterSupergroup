using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Combat;
using AstralShift.Initialization;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.Scenes.SceneLoaders
{
	public class PoolLoader : SceneLoader
	{
		[SerializeField]
		private PoolManager poolManager;

		[SerializeField]
		private EnemyStatusResolver enemyStatusResolver;

		[SerializeField]
		private EquipmentEffectResolver equipmentEffectResolver;

		public override UniTask LoadAsync()
		{
			poolManager.Init();
			enemyStatusResolver.Init();
			equipmentEffectResolver.Init();
			return UniTask.CompletedTask;
		}
	}
}
