using UnityEngine;

namespace AstralShift.Pooling
{
	public class GenericPooler<T> where T : Object
	{
		private T _prefab;

		private BasePooler<T> _basePooler;

		public GenericPooler(T prefab, string name, Transform parent, int capacity = -1)
		{
			_prefab = prefab;
			_basePooler = new BasePooler<T>(name, parent, capacity);
		}

		public virtual T GetOrCreate(bool activate = false)
		{
			return GetOrCreate(_basePooler.Parent, activate);
		}

		public virtual T GetOrCreate(Transform parent, bool activate = false)
		{
			if (_basePooler.Get(out var element))
			{
				if (!element)
				{
					return Object.Instantiate(_prefab, parent);
				}
				if (element is Component component)
				{
					component.transform.SetParent(parent);
					if (activate)
					{
						component.gameObject.SetActive(value: true);
					}
				}
				if (element is GameObject gameObject)
				{
					gameObject.transform.SetParent(parent);
					if (activate)
					{
						gameObject.SetActive(value: true);
					}
				}
				return element;
			}
			return Object.Instantiate(_prefab, parent);
		}

		public virtual void Return(T element, bool deactivate = true)
		{
			if (element is GameObject gameObject)
			{
				if (deactivate)
				{
					gameObject.SetActive(value: false);
				}
				gameObject.transform.SetParent(_basePooler.Parent, worldPositionStays: true);
			}
			if (element is Component component)
			{
				if (deactivate)
				{
					component.gameObject.SetActive(value: false);
				}
				component.transform.SetParent(_basePooler.Parent, worldPositionStays: true);
			}
			_basePooler.Return(element);
		}

		public virtual void Clear(bool destroy = true)
		{
			Object.Destroy(_basePooler.Parent);
			_basePooler.Clear();
		}
	}
}
