using System;
using System.Collections;
using System.Collections.Generic;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Scenes;
using AstralShift.Helpers;
using AstralShift.Helpers.Collections;
using AstralShift.Managers;
using AstralShift.Pooling;
using UnityEngine;

namespace AstralShift.HellMaiden.Items
{
	public class LootManager : MonoBehaviour, IPausable
	{
		[Serializable]
		public class XPDropControl
		{
			public enum DropType
			{
				None = 0,
				XP = 1,
				Item = 2
			}

			[Range(0f, 1f)]
			public float xpWeight = 0.5f;

			[Range(0f, 1f)]
			public float itemWeight = 0.4f;

			private float xpAccum = 2f;

			private float itemAccum;

			[SerializeField]
			private float lowHpBias;

			public DropType GetNextDrop()
			{
				if (xpWeight + itemWeight == 0f)
				{
					return DropType.None;
				}
				xpAccum += xpWeight;
				itemAccum += itemWeight;
				if (xpAccum >= 1f)
				{
					xpAccum -= 1f;
					return DropType.XP;
				}
				float t = 1f - GameDirector.Instance.Player.PlayerStats.GetHealthPercentage();
				float num = Mathf.Lerp(0f, lowHpBias, t);
				if (UnityEngine.Random.Range(0f, 1f) <= itemAccum + num)
				{
					itemAccum = 0f;
					return DropType.Item;
				}
				return DropType.None;
			}

			public void Reset()
			{
				xpAccum = 2f;
				itemAccum = 0f;
			}

			public void SetLootWeightValues(float newXpWeight, float newItemWeight)
			{
				xpWeight = newXpWeight;
				itemWeight = newItemWeight;
			}
		}

		public static LootManager Instance;

		public LootSettingsData.ItemSettingsOverride items;

		[SerializeField]
		private WorldChest chestPrefab;

		private GenericPooler<WorldChest> chestPool;

		[Header("XP Garbage Collection Settings")]
		[SerializeField]
		private float XPGarbageCollectionInterval = 0.5f;

		[SerializeField]
		private bool XPGarbageCollectionEnabled = true;

		[SerializeField]
		private float timeoutForGarbageCollection = 60f;

		private Dictionary<XPGem, float> _garbageCollectionTimeTracker;

		private List<ILootColector> _lootCollectors;

		private List<WorldItem> _toBeCollectedLoot;

		private List<WorldItem> _toBeConsumedLoot;

		private Queue<WorldItem> _consumeQueue;

		private bool _isCollectorsPullPaused;

		[Header("XP Drop Control")]
		[SerializeField]
		private XPDropControl xpDropControl = new XPDropControl();

		private bool _ultimateCurrentlySpawned;

		[Header("Biases")]
		public float lowHealthBias = 0.5f;

		public float magnetTimeBias = 0.5f;

		public int magnetMinXpToCollect = 20;

		public List<ILootColector> LootCollectors => _lootCollectors;

		public List<WorldItem> ToBeCollectedLoot => _toBeCollectedLoot;

		public List<WorldItem> ToBeConsumedLoot => _toBeConsumedLoot;

		public Queue<WorldItem> ConsumeQueue => _consumeQueue;

		public bool UltimateCurrentlySpawned
		{
			get
			{
				return _ultimateCurrentlySpawned;
			}
			set
			{
				_ultimateCurrentlySpawned = value;
			}
		}

		public event Action<WorldItem> OnItemSpawned;

		public event Action<WorldItem> OnItemDespawned;

		private void Start()
		{
			if (Instance == null)
			{
				Instance = this;
			}
			else
			{
				UnityEngine.Object.Destroy(this);
			}
			_garbageCollectionTimeTracker = new Dictionary<XPGem, float>();
			_toBeCollectedLoot = new List<WorldItem>();
			_toBeConsumedLoot = new List<WorldItem>();
			_consumeQueue = new Queue<WorldItem>();
			_lootCollectors = new List<ILootColector>();
			if (GameDirector.Instance?.Player != null)
			{
				RegisterLootCollector(GameDirector.Instance.Player);
			}
			if (XPGarbageCollectionEnabled)
			{
				StartXPGarbageCollection();
			}
			UltimateCurrentlySpawned = false;
			((IPausable)this).Subscribe();
			SceneMaster.Instance.OnSceneHideStart += StopAllItemsPull;
			SceneMaster.Instance.OnSceneHideStart += StopConsume;
		}

		private void OnDestroy()
		{
			((IPausable)this).UnSubscribe();
		}

		public void RegisterLootCollector(ILootColector collector)
		{
			if (collector != null && !_lootCollectors.Contains(collector))
			{
				_lootCollectors.Add(collector);
			}
		}

		public void UnRegisterLootCollector(ILootColector collector)
		{
			_lootCollectors.Remove(collector);
		}

		public void RegisterSpawnedItem(WorldItem item)
		{
			_toBeCollectedLoot.AddIfNotNull(item);
			this.OnItemSpawned?.Invoke(item);
		}

		public void UnRegisterSpawnedItem(WorldItem item)
		{
			this.OnItemDespawned?.Invoke(item);
			_toBeCollectedLoot.Remove(item);
			_toBeConsumedLoot.Remove(item);
		}

		public bool TryStartConsumePull(WorldItem item, ILootColector collector)
		{
			bool num = item.StartPlayerPull(collector);
			if (num)
			{
				_toBeCollectedLoot.Remove(item);
				_toBeConsumedLoot.Add(item);
			}
			return num;
		}

		public bool TryStartConsumePull(WorldItem item)
		{
			if (item == null)
			{
				return false;
			}

			ILootColector closestCollector = null;
			float closestSqrDistance = float.MaxValue;
			for (int i = 0; i < _lootCollectors.Count; i++)
			{
				ILootColector collector = _lootCollectors[i];
				if (collector?.CombatantBinding == null ||
					!collector.CombatantBinding.AcceptsLocalMutations)
				{
					continue;
				}

				float sqrDistance = ((Vector2)item.transform.position -
					collector.GetLootCollectorPosition()).sqrMagnitude;
				if (sqrDistance < closestSqrDistance)
				{
					closestSqrDistance = sqrDistance;
					closestCollector = collector;
				}
			}

			return closestCollector != null &&
				TryStartConsumePull(item, closestCollector);
		}

		public void EnqueueConsume(WorldItem item)
		{
			_consumeQueue.Enqueue(item);
		}

		private void Update()
		{
			if (PlayerState.IsBusy() || _isCollectorsPullPaused)
			{
				return;
			}
			for (int num = _toBeCollectedLoot.Count - 1; num >= 0; num--)
			{
				Vector2 vector = _toBeCollectedLoot[num].transform.position;
				int num2 = _lootCollectors.Count - 1;
				while (num2 >= 0 && ((vector - _lootCollectors[num2].GetLootCollectorPosition()).sqrMagnitude > Mathf.Pow(_lootCollectors[num2].GetLootPullArea(), 2f) || !TryStartConsumePull(_toBeCollectedLoot[num], _lootCollectors[num2])))
				{
					num2--;
				}
			}
			if (_consumeQueue.Count > 0)
			{
				_consumeQueue.Dequeue().Consume();
			}
		}

		public List<WorldItem> GetOverridenLoot(float xpValue, LootSettingsData settings)
		{
			List<WorldItem> list = new List<WorldItem>();
			if (xpValue <= 0f)
			{
				return null;
			}
			if (settings.isXPMandatory)
			{
				list.Add(GetXP(xpValue));
				for (int i = 0; i < settings.ItemSettings.numberOfItems; i++)
				{
					list.AddIfNotNull(GetWeightedRandomItem(settings.ItemSettings));
				}
			}
			else
			{
				float num = settings.XPWeight + settings.ItemsWeight;
				float num2 = UnityEngine.Random.Range(0f, 1f);
				if (settings.alwaysDrops)
				{
					float num3 = settings.XPWeight / num;
					if (num2 < num3)
					{
						list.Add(GetXP(xpValue));
					}
					else
					{
						list.AddIfNotNull(GetWeightedRandomItem(settings.ItemSettings));
					}
				}
				else if (num2 < settings.XPWeight)
				{
					list.Add(GetXP(xpValue));
				}
				else if (num2 < settings.XPWeight + settings.ItemsWeight)
				{
					list.AddIfNotNull(GetWeightedRandomItem(settings.ItemSettings));
				}
			}
			return list;
		}

		public WorldItem GetGlobalLoot(float xpValue)
		{
			switch (xpDropControl.GetNextDrop())
			{
			case XPDropControl.DropType.XP:
				if (xpValue > 0f)
				{
					return GetXP(xpValue);
				}
				break;
			case XPDropControl.DropType.Item:
				return GetWeightedRandomItem(items);
			}
			return null;
		}

		public WorldItem GetXP(float value)
		{
			XPGem xPGem = PoolManager.Instance.xpPool.Get(value);
			_garbageCollectionTimeTracker.Remove(xPGem);
			RegisterSpawnedItem(xPGem);
			return xPGem;
		}

		public WorldChest GetChest()
		{
			chestPool = PoolManager.Instance.GetOrCreatePooler(chestPrefab);
			return chestPool.GetOrCreate(activate: true);
		}

		public WorldItem GetWorldItem(WorldItem item)
		{
			WorldItem worldItem = item;
			if (!(worldItem is HealthItem))
			{
				if (!(worldItem is MagnetItem))
				{
					if (worldItem is UltimateItem)
					{
						if (UltimateCurrentlySpawned || GameDirector.Instance.Player.HasUltimateCharge)
						{
							return null;
						}
						_ultimateCurrentlySpawned = true;
						PoolManager.Instance.ItemsPool.UltimatePowerup.TryGet(out item);
						RegisterSpawnedItem(item);
						return item;
					}
					Debug.LogWarning("Item doesnt match existing item types");
					return null;
				}
				PoolManager.Instance.ItemsPool.Magnet.TryGet(out item);
				RegisterSpawnedItem(item);
				return item;
			}
			PoolManager.Instance.ItemsPool.Health.TryGet(out item);
			RegisterSpawnedItem(item);
			return item;
		}

		private WorldItem GetWeightedRandomItem(LootSettingsData.ItemSettingsOverride settings)
		{
			float num = 0f;
			float healthPercentage = GameDirector.Instance.Player.PlayerStats.GetHealthPercentage();
			float num2 = Mathf.Lerp(0f, lowHealthBias, 1f - healthPercentage);
			float num3 = settings.HealthWeight + num2;
			float num4 = 0f;
			if (_toBeCollectedLoot.Count > magnetMinXpToCollect)
			{
				float num5 = Mathf.Lerp(0f, magnetTimeBias, ProgressionManager.Instance.ProgressionPercent);
				num4 = settings.MagnetWeight + num5;
			}
			num += settings.HealthWeight;
			num += settings.MagnetWeight;
			if (num > 0f)
			{
				num3 /= num;
				num4 /= num;
			}
			float num6 = UnityEngine.Random.Range(0f, 1f);
			float num7 = 0f;
			num7 += num3;
			WorldItem item;
			if (num6 < num7)
			{
				num7 -= num3;
				if (PoolManager.Instance.ItemsPool.Health.TryGet(out item))
				{
					RegisterSpawnedItem(item);
					return item;
				}
			}
			num7 += num4;
			if (num6 < num7)
			{
				num7 -= num4;
				if (PoolManager.Instance.ItemsPool.Magnet.TryGet(out item))
				{
					RegisterSpawnedItem(item);
					return item;
				}
			}
			PoolManager.Instance.ItemsPool.Health.TryGet(out item);
			RegisterSpawnedItem(item);
			return item;
		}

		public void StartXPGarbageCollection()
		{
			XPGarbageCollectionEnabled = true;
			StartCoroutine(XPGemsGarbageCollector());
		}

		public void DisableXPGarbageCollection()
		{
			XPGarbageCollectionEnabled = false;
		}

		private IEnumerator XPGemsGarbageCollector()
		{
			WaitForSeconds wait = new WaitForSeconds(XPGarbageCollectionInterval);
			while (XPGarbageCollectionEnabled)
			{
				yield return wait;
				List<WorldItem> list = _toBeCollectedLoot.FindAll((WorldItem x) => x is XPGem);
				if (list.Count == 0)
				{
					continue;
				}
				int count = list.Count;
				for (int num = 0; num < count; num++)
				{
					XPGem xPGem = list[num] as XPGem;
					if (ProCamera2DHelpers.IsWithinCameraBounds(xPGem.transform.position))
					{
						_garbageCollectionTimeTracker.Remove(xPGem);
						PoolManager.Instance.xpPool.SetGemAsActive(xPGem);
						continue;
					}
					_garbageCollectionTimeTracker.TryAdd(xPGem, Time.time);
					if (Time.time - _garbageCollectionTimeTracker[xPGem] >= timeoutForGarbageCollection + 0.01f)
					{
						PoolManager.Instance.xpPool.SetGemAsInactive(xPGem);
					}
				}
			}
		}

		public void ResumeItemsPull()
		{
			_isCollectorsPullPaused = false;
			for (int i = 0; i < _toBeConsumedLoot.Count; i++)
			{
				_toBeConsumedLoot[i].ResumePlayerPull();
			}
		}

		public void PauseItemsPull()
		{
			_isCollectorsPullPaused = true;
			for (int i = 0; i < _toBeConsumedLoot.Count; i++)
			{
				_toBeConsumedLoot[i].PausePlayerPull();
			}
		}

		public void StopAllItemsPull()
		{
			_isCollectorsPullPaused = true;
		}

		public void StopConsume()
		{
			_consumeQueue.Clear();
		}

		public void DisposeAllItems()
		{
			for (int num = _toBeCollectedLoot.Count - 1; num >= 0; num--)
			{
				_toBeCollectedLoot[num].Dispose();
			}
			_toBeCollectedLoot.Clear();
			for (int num2 = _toBeConsumedLoot.Count - 1; num2 >= 0; num2--)
			{
				_toBeConsumedLoot[num2].Dispose();
			}
			_toBeConsumedLoot.Clear();
		}

		public void OnPausePausables()
		{
			PauseItemsPull();
		}

		public void OnResumePausables()
		{
			ResumeItemsPull();
		}

		public void OnGamePause()
		{
			PauseItemsPull();
		}

		public void OnGameResume()
		{
			ResumeItemsPull();
		}
	}
}
