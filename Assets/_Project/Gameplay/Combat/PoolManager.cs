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
		private readonly HashSet<DamageNumber> networkDamageNumbers = new HashSet<DamageNumber>();
		public bool NetworkDamageNumbersEnabled { get; set; } = true;

		public void Init()
		{
			Instance = this;
		}

		private void OnDestroy()
		{
			ClearNetworkDamageNumbers();
			ClearAllPoolers();
			if (Instance == this) Instance = null;
		}

		public static string NetworkDamageNumberGroup(uint sourcePlayerId, uint damageSourceId,
			uint targetEntityId, DamageType type, bool critical) =>
			$"network:{sourcePlayerId}:{damageSourceId}:{targetEntityId}:{(int)type}:{critical}";

		public bool TrySpawnNetworkDamageNumber(Vector3 position, Transform target, int damage,
			DamageType type, bool critical, uint sourcePlayerId, uint damageSourceId, uint targetEntityId)
		{
			if (!NetworkDamageNumbersEnabled || damageColors == null || damage <= 0) return false;
			// Thorns/projectiles share the normal style, including its critical variant.
			DamageType style = type == DamageType.Thorns || type == DamageType.Projectile ? DamageType.Normal : type;
			DamageNumber prefab = damageColors.GetDamageTypeColor(style, critical);
			if (prefab == null) return false;
			DamageNumber popup = prefab.Spawn(position, damage, target);
			if (popup == null) return false;
			popup.SetSpamGroup(NetworkDamageNumberGroup(sourcePlayerId, damageSourceId, targetEntityId, type, critical));
			networkDamageNumbers.RemoveWhere(number => number == null);
			networkDamageNumbers.Add(popup);
			return true;
		}

		public void ClearNetworkDamageNumbers()
		{
			// Includes our active and pooled instances; unrelated popups keep their lifecycle.
			foreach (DamageNumber number in networkDamageNumbers)
				if (number != null) Destroy(number.gameObject);
			networkDamageNumbers.Clear();
		}

		public void SpawnDamageNumber(int damageableID, Transform targetTransform, int number, DamageType damageType, bool isCritical)
		{
			if (CanSpawnDamageNumbers())
			{
				string spamGroup = $"{damageType.ToString()} : {damageableID}";
				damageColors.GetDamageTypeColor(damageType, isCritical).Spawn(targetTransform.position, number, targetTransform).SetSpamGroup(spamGroup);
			}
		}

		public void SpawnDamageNumber(int sourceID, int damageableID, Transform targetTransform, int number, DamageType damageType, bool isCritical)
		{
			if (CanSpawnDamageNumbers())
			{
				string spamGroup = $"{damageType.ToString()} : {sourceID} : {damageableID}";
				damageColors.GetDamageTypeColor(damageType, isCritical).Spawn(targetTransform.position, number, targetTransform).SetSpamGroup(spamGroup);
			}
		}

		private bool CanSpawnDamageNumbers()
		{
			return damageColors != null && GameDirector.Instance != null &&
				GameDirector.Instance.Settings != null &&
				GameDirector.Instance.Settings.DamageNumbers;
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
