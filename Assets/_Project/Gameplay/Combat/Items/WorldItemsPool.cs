using System;
using AstralShift.Pooling;
using UnityEngine;

namespace AstralShift.HellMaiden.Items
{
	public class WorldItemsPool : MonoBehaviour
	{
		[Serializable]
		public class WorldItemPool
		{
			private string name;

			public WorldItem prefab;

			private Transform _parent;

			public int poolSize;

			private BasePooler<WorldItem> _pool;

			[Tooltip("Maximum amount of this item spawned at the same time.")]
			public int itemMax = 10;

			[Tooltip("Current amount of this item spawned.")]
			public int itemAmount;

			[Tooltip("Maximum amount of this item that can spawn in total. Gets decreased during gameplay.")]
			public int itemMaxTotal = int.MaxValue;

			public void Init(string name, Transform transform)
			{
				this.name = name;
				GameObject gameObject = new GameObject(this.name);
				gameObject.transform.SetParent(transform);
				_parent = gameObject.transform;
				_pool = new BasePooler<WorldItem>(this.name, _parent, poolSize);
			}

			public bool TryGet(out WorldItem item)
			{
				item = null;
				if (itemAmount >= itemMax || itemMaxTotal < 1)
				{
					return false;
				}
				if (_pool == null)
				{
					_pool = new BasePooler<WorldItem>(name, _parent, poolSize);
				}
				if (!_pool.Get(out item))
				{
					item = UnityEngine.Object.Instantiate(prefab, _parent);
				}
				itemAmount++;
				itemMaxTotal--;
				return true;
			}

			public void Return(WorldItem item)
			{
				_pool.Return(item);
				item.gameObject.SetActive(value: false);
			}
		}

		public WorldItemPool Health;

		public WorldItemPool Magnet;

		public WorldItemPool UltimatePowerup;

		private void Awake()
		{
			Health.Init("Health", base.transform);
			Magnet.Init("Magnet", base.transform);
			UltimatePowerup.Init("UltimatePowerup", base.transform);
		}
	}
}
