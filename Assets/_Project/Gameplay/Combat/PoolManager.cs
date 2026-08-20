using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Items;
using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.Pooling;
using DamageNumbersPro;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat
{
	public class PoolManager2 : MonoBehaviour
	{
	}

	public class PoolManager : MonoBehaviour
	{
		public static PoolManager Instance;

		public XPPool xpPool;

		public WorldItemsPool ItemsPool;

		private readonly Dictionary<Type, Dictionary<int, object>> _poolers = new Dictionary<Type, Dictionary<int, object>>();

		[SerializeField]
		private DamageColorsSO damageColors;

		public void Init()
		{
			Instance = this;
		}

		private void OnDestroy()
		{
			ClearAllPoolers();
		}

		public void SpawnDamageNumber(int damageableID, Transform targetTransform, int number, DamageType damageType, bool isCritical)
		{
			if (GameDirector.Instance.Settings.DamageNumbers)
			{
				string spamGroup = $"{damageType.ToString()} : {damageableID}";
				damageColors.GetDamageTypeColor(damageType, isCritical).Spawn(targetTransform.position, number, targetTransform).SetSpamGroup(spamGroup);
			}
		}

		public void SpawnDamageNumber(int sourceID, int damageableID, Transform targetTransform, int number, DamageType damageType, bool isCritical)
		{
			if (GameDirector.Instance.Settings.DamageNumbers)
			{
				string spamGroup = $"{damageType.ToString()} : {sourceID} : {damageableID}";
				damageColors.GetDamageTypeColor(damageType, isCritical).Spawn(targetTransform.position, number, targetTransform).SetSpamGroup(spamGroup);
			}
		}

		public GenericPooler<T> GetOrCreatePooler<T>(T prefab, int capacity = -1) where T : UnityEngine.Object
		{
			if (prefab == null)
			{
				return null;
			}
			Type typeFromHandle = typeof(T);
			if (!_poolers.TryGetValue(typeFromHandle, out var value))
			{
				value = new Dictionary<int, object>();
				_poolers[typeFromHandle] = value;
			}
			int instanceID = prefab.GetInstanceID();
			if (value.TryGetValue(instanceID, out var value2))
			{
				return (GenericPooler<T>)value2;
			}
			Transform parent = GeneratePoolParent($"Pool: {prefab.name} (ID: {instanceID})");
			return (GenericPooler<T>)(value[instanceID] = new GenericPooler<T>(prefab, prefab.name, parent, capacity));
		}

		private void ClearAllPoolers()
		{
			_poolers.Clear();
			DamageNumber.ClearPooled();
		}

		public Transform GeneratePoolParent(string poolName)
		{
			Transform obj = new GameObject(poolName).transform;
			obj.parent = base.transform;
			obj.name = poolName;
			return obj;
		}
	}
}
