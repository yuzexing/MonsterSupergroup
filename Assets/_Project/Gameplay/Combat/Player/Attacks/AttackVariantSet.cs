using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Combat;
using AstralShift.Pooling;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	[Serializable]
	public class AttackVariantSet<T> where T : MonoBehaviour
	{
		[SerializeField]
		private T defaultPrefab;

		[SerializeField]
		private T poisonPrefab;

		[SerializeField]
		private T firePrefab;

		[Tooltip("Debug toggle. Poison is only used when its prefab is assigned AND this is on.")]
		[SerializeField]
		private bool allowPoison = true;

		[Tooltip("Debug toggle. Fire is only used when its prefab is assigned AND this is on.")]
		[SerializeField]
		private bool allowFire = true;

		private GenericPooler<T> _defaultPooler;

		private GenericPooler<T> _poisonPooler;

		private GenericPooler<T> _firePooler;

		private readonly List<T> _defaultInstances = new List<T>();

		private readonly List<T> _poisonInstances = new List<T>();

		private readonly List<T> _fireInstances = new List<T>();

		public void Init()
		{
			_defaultPooler = PoolManager.Instance.GetOrCreatePooler(defaultPrefab);
			_poisonPooler = ((allowPoison && poisonPrefab != null) ? PoolManager.Instance.GetOrCreatePooler(poisonPrefab) : null);
			_firePooler = ((allowFire && firePrefab != null) ? PoolManager.Instance.GetOrCreatePooler(firePrefab) : null);
		}

		public T GetOrCreate(AttackElement element, Transform parent)
		{
			List<T> instances;
			T orCreate = ResolvePooler(element, out instances).GetOrCreate(parent);
			Track(instances, orCreate);
			return orCreate;
		}

		public T GetOrCreate(AttackElement element, Transform parent, bool worldPositionStays)
		{
			List<T> instances;
			T orCreate = ResolvePooler(element, out instances).GetOrCreate(parent, worldPositionStays);
			Track(instances, orCreate);
			return orCreate;
		}

		public T GetOrCreate(AttackElement element, bool worldPositionStays)
		{
			List<T> instances;
			T orCreate = ResolvePooler(element, out instances).GetOrCreate(worldPositionStays);
			Track(instances, orCreate);
			return orCreate;
		}

		public AttackElement ResolveElement(AttackElement requested)
		{
			switch (requested)
			{
			case AttackElement.Fire:
				if (_firePooler != null)
				{
					return AttackElement.Fire;
				}
				break;
			case AttackElement.Poison:
				if (_poisonPooler != null)
				{
					return AttackElement.Poison;
				}
				break;
			}
			return AttackElement.Default;
		}

		public void Return(T attack)
		{
			if (!TryReturn(_fireInstances, _firePooler, attack) && !TryReturn(_poisonInstances, _poisonPooler, attack))
			{
				TryReturn(_defaultInstances, _defaultPooler, attack);
			}
		}

		public void Dispose()
		{
			ReturnAll(_defaultInstances, _defaultPooler);
			ReturnAll(_poisonInstances, _poisonPooler);
			ReturnAll(_fireInstances, _firePooler);
		}

		public void Dispose(Action<T> disposeEach)
		{
			DisposeTracked(_defaultInstances, _defaultPooler, disposeEach);
			DisposeTracked(_poisonInstances, _poisonPooler, disposeEach);
			DisposeTracked(_fireInstances, _firePooler, disposeEach);
		}

		private static void DisposeTracked(List<T> instances, GenericPooler<T> pooler, Action<T> disposeEach)
		{
			if (pooler != null && instances.Count != 0)
			{
				T[] array = instances.ToArray();
				instances.Clear();
				for (int i = 0; i < array.Length; i++)
				{
					disposeEach?.Invoke(array[i]);
					pooler.Return(array[i]);
				}
			}
		}

		private GenericPooler<T> ResolvePooler(AttackElement element, out List<T> instances)
		{
			switch (element)
			{
			case AttackElement.Fire:
				if (_firePooler != null)
				{
					instances = _fireInstances;
					return _firePooler;
				}
				break;
			case AttackElement.Poison:
				if (_poisonPooler != null)
				{
					instances = _poisonInstances;
					return _poisonPooler;
				}
				break;
			}
			instances = _defaultInstances;
			return _defaultPooler;
		}

		private static void Track(List<T> instances, T attack)
		{
			if (!instances.Contains(attack))
			{
				instances.Add(attack);
			}
		}

		private static bool TryReturn(List<T> instances, GenericPooler<T> pooler, T attack)
		{
			if (pooler == null || !instances.Remove(attack))
			{
				return false;
			}
			pooler.Return(attack);
			return true;
		}

		private static void ReturnAll(List<T> instances, GenericPooler<T> pooler)
		{
			if (pooler != null)
			{
				for (int num = instances.Count - 1; num >= 0; num--)
				{
					pooler.Return(instances[num]);
				}
				instances.Clear();
			}
		}
	}
}
