using UnityEngine;

namespace AstralShift.Pooling
{
	public class GenericGameObjectPool : MonoBehaviour
	{
		public GameObject prefab;

		public Transform poolParent;

		public int poolSize = 100;

		private BasePooler<GameObject> _pool;

		public GameObject Get(Transform parent = null)
		{
			if (_pool == null)
			{
				_pool = new BasePooler<GameObject>(prefab.name + " pool", poolParent, poolSize);
			}
			if (!_pool.Get(out var element))
			{
				if (parent == null)
				{
					return Object.Instantiate(prefab, poolParent);
				}
				return Object.Instantiate(prefab, parent);
			}
			element.transform.SetParent(parent);
			return element;
		}

		public void Return(GameObject obj, bool parent = false)
		{
			if (parent)
			{
				obj.transform.SetParent(_pool.Parent);
			}
			_pool.Return(obj);
		}
	}
}
