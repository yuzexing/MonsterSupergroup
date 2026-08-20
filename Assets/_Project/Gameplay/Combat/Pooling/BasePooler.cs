using System.Collections.Generic;
using UnityEngine;

namespace AstralShift.Pooling
{
	public class BasePooler<T>
	{
		protected string name;

		protected Transform _parent;

		protected Queue<T> _pool;

		protected int _maxCapacity = 100;

		public Transform Parent => _parent;

		public BasePooler(string name, Transform parent, int capacity = -1)
		{
			this.name = name;
			_parent = parent;
			if (capacity != -1)
			{
				_maxCapacity = capacity;
			}
			_pool = new Queue<T>(_maxCapacity);
		}

		public virtual bool IsAlreadyPooled(T element)
		{
			return _pool.Contains(element);
		}

		public virtual bool Get(out T element)
		{
			_pool.TryDequeue(out element);
			return element != null;
		}

		public virtual void Clear()
		{
			_pool.Clear();
		}

		public virtual void Return(T element)
		{
			if (!_pool.Contains(element))
			{
				if (_pool.Count == _maxCapacity)
				{
					Debug.LogWarning("POOL <" + name.ToUpperInvariant() + ">: Reached Maximum Capacity! Can't add objects.");
				}
				else
				{
					_pool.Enqueue(element);
				}
			}
		}
	}
}
