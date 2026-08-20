using AstralShift.Pooling;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class EnemyPool : MonoBehaviour
	{
		public EnemyController enemyPrefab;

		public Transform poolParent;

		public int poolSize = 100;

		private BasePooler<EnemyController> _pool;

		public EnemyController Get()
		{
			if (_pool == null)
			{
				_pool = new BasePooler<EnemyController>("EnemyPool", poolParent, poolSize);
			}
			if (!_pool.Get(out var element))
			{
				return Object.Instantiate(enemyPrefab, poolParent);
			}
			return element;
		}

		public void Return(EnemyController enemy)
		{
			if (_pool == null)
			{
				_pool = new BasePooler<EnemyController>("EnemyPool", poolParent, poolSize);
			}
			_pool.Return(enemy);
		}
	}
}
