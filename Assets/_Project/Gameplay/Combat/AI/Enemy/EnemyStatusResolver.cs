using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.Pooling;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class EnemyStatusResolver : MonoBehaviour
	{
		public enum onHitTransform
		{
			Center = 0,
			Center1 = 1,
			Center2 = 2,
			Center3 = 3,
			Top = 4,
			Bottom = 5
		}

		public enum StackMode
		{
			Add = 0,
			Replace = 1,
			HighestPriority = 2
		}

		private class StatusHandler
		{
			private readonly GenericPooler<GameObject> _pooler;

			private readonly onHitTransform _hitTransform;

			private readonly int _maxStacks;

			private readonly StackMode _strategy;

			private readonly Action<BaseEnemyController, float> _onApply;

			private readonly Action<BaseEnemyController> _onRemove;

			private readonly Action<BaseEnemyController, int> _onTick;

			private readonly List<BaseEnemyController> _activeEnemies;

			private readonly Dictionary<BaseEnemyController, List<EnemyStatusData>> _tracker;

			private readonly Dictionary<BaseEnemyController, GameObject> _visuals;

			private static readonly int EffectAnimHash = Animator.StringToHash("EffectAnim");

			public StatusHandler(GameObject prefab, onHitTransform hitTransform, int maxStacks, StackMode strategy, Action<BaseEnemyController, float> onApply, Action<BaseEnemyController> onRemove, Action<BaseEnemyController, int> onTick = null)
			{
				_pooler = PoolManager.Instance.GetOrCreatePooler(prefab);
				_hitTransform = hitTransform;
				_maxStacks = maxStacks;
				_strategy = strategy;
				_onApply = onApply;
				_onRemove = onRemove;
				_onTick = onTick;
				_activeEnemies = new List<BaseEnemyController>();
				_tracker = new Dictionary<BaseEnemyController, List<EnemyStatusData>>();
				_visuals = new Dictionary<BaseEnemyController, GameObject>();
			}

			public void Register(BaseEnemyController enemy, EnemyStatusData data)
			{
				if (!_tracker.ContainsKey(enemy))
				{
					_tracker[enemy] = new List<EnemyStatusData>();
					_activeEnemies.Add(enemy);
					GameObject orCreate = _pooler.GetOrCreate(activate: true);
					SetEffectPosition(orCreate, enemy, _hitTransform);
					_visuals[enemy] = orCreate;
					if (orCreate.TryGetComponent<Animator>(out var component))
					{
						component.Play(EffectAnimHash, -1, 0f);
						if (component.runtimeAnimatorController.animationClips.Length != 0)
						{
							float num = ((data.totalDuration > 0f) ? data.totalDuration : 1f);
							float length = component.runtimeAnimatorController.animationClips[0].length;
							component.speed = length / num;
						}
					}
				}
				List<EnemyStatusData> list = _tracker[enemy];
				switch (_strategy)
				{
				case StackMode.Add:
					if (list.Count < _maxStacks)
					{
						list.Add(data);
						_onApply?.Invoke(enemy, data.power);
					}
					break;
				case StackMode.Replace:
					list.Clear();
					list.Add(data);
					_onApply?.Invoke(enemy, data.power);
					break;
				case StackMode.HighestPriority:
					if (list.Count > 0)
					{
						if (data.priority >= list[0].priority)
						{
							data.startTime = list[0].startTime;
							list[0] = data;
							_onApply?.Invoke(enemy, data.power);
						}
					}
					else
					{
						list.Add(data);
						_onApply?.Invoke(enemy, data.power);
					}
					break;
				}
			}

			public void UnRegister(BaseEnemyController enemy)
			{
				if (_tracker.ContainsKey(enemy))
				{
					RemoveEffect(enemy);
					_activeEnemies.Remove(enemy);
				}
			}

			public void Update(float currentTime)
			{
				for (int num = _activeEnemies.Count - 1; num >= 0; num--)
				{
					BaseEnemyController baseEnemyController = _activeEnemies[num];
					if (baseEnemyController == null || !baseEnemyController.gameObject.activeInHierarchy)
					{
						UnRegister(baseEnemyController);
					}
					else
					{
						List<EnemyStatusData> list = _tracker[baseEnemyController];
						for (int num2 = list.Count - 1; num2 >= 0; num2--)
						{
							EnemyStatusData value = list[num2];
							bool flag = false;
							bool flag2 = false;
							if (_onTick != null)
							{
								if (currentTime - value.startTime >= value.hitInterval)
								{
									_onTick(baseEnemyController, (int)value.power);
									value.currentDuration += 1f;
									value.startTime = currentTime;
									flag2 = true;
									if (value.currentDuration >= value.totalDuration)
									{
										flag = true;
									}
								}
							}
							else if (currentTime - value.startTime >= value.totalDuration)
							{
								flag = true;
							}
							if (flag)
							{
								list.RemoveAt(num2);
							}
							else if (flag2)
							{
								list[num2] = value;
							}
						}
						if (list.Count == 0)
						{
							RemoveEffect(baseEnemyController);
							_activeEnemies.RemoveAt(num);
						}
					}
				}
			}

			public void ConsumeStack(BaseEnemyController enemy)
			{
				if (!_tracker.TryGetValue(enemy, out var value))
				{
					return;
				}
				if (_onTick != null)
				{
					for (int num = value.Count - 1; num >= 0; num--)
					{
						_onTick(enemy, (int)value[num].power);
					}
				}
				value.Clear();
				RemoveEffect(enemy);
				_activeEnemies.Remove(enemy);
			}

			private void RemoveEffect(BaseEnemyController enemy)
			{
				if (_visuals.TryGetValue(enemy, out var value))
				{
					_pooler.Return(value);
					_visuals.Remove(enemy);
				}
				_tracker.Remove(enemy);
				_onRemove?.Invoke(enemy);
			}

			public void TransferEffect(BaseEnemyController source, BaseEnemyController target)
			{
				if (_tracker.TryGetValue(source, out var value))
				{
					_tracker[target] = new List<EnemyStatusData>(value);
					_activeEnemies.Add(target);
					if (_visuals.TryGetValue(source, out var value2))
					{
						_visuals[target] = value2;
						SetEffectPosition(value2, target, _hitTransform);
						_visuals.Remove(source);
					}
					if (value.Count > 0)
					{
						_onApply?.Invoke(target, value[0].power);
					}
					_tracker.Remove(source);
					_activeEnemies.Remove(source);
				}
			}

			private void SetEffectPosition(GameObject effect, BaseEnemyController enemy, onHitTransform hitTransform)
			{
				Transform transform = enemy.Transform;
				switch (hitTransform)
				{
				case onHitTransform.Center:
					transform = (enemy.OnHitEffectCenterPivot ? enemy.OnHitEffectCenterPivot : transform);
					break;
				case onHitTransform.Center1:
					transform = (enemy.OnHitEffectCenterPivot1 ? enemy.OnHitEffectCenterPivot1 : transform);
					break;
				case onHitTransform.Center2:
					transform = (enemy.OnHitEffectCenterPivot2 ? enemy.OnHitEffectCenterPivot2 : transform);
					break;
				case onHitTransform.Center3:
					transform = (enemy.OnHitEffectCenterPivot3 ? enemy.OnHitEffectCenterPivot3 : transform);
					break;
				case onHitTransform.Top:
					transform = (enemy.OnHitEffectTopPivot ? enemy.OnHitEffectTopPivot : transform);
					break;
				case onHitTransform.Bottom:
					transform = (enemy.OnHitEffectBottomPivot ? enemy.OnHitEffectBottomPivot : transform);
					break;
				}
				effect.transform.position = transform.position;
				effect.transform.SetParent(transform);
			}
		}

		[Header("Prefabs")]
		public GameObject SlowEffectPrefab;

		public GameObject BurnEffectPrefab;

		public GameObject PoisonEffectPrefab;

		public GameObject BleedEffectPrefab;

		public GameObject WeakEffectPrefab;

		[Header("Transforms")]
		public onHitTransform slowTransform;

		public onHitTransform burnTransform;

		public onHitTransform poisonTransform;

		public onHitTransform bleedTransform;

		public onHitTransform weakTransform;

		private StatusHandler _slowHandler;

		private StatusHandler _burnHandler;

		private StatusHandler _poisonHandler;

		private StatusHandler _bleedHandler;

		private StatusHandler _weakHandler;

		private bool _isInitialized;

		public static EnemyStatusResolver Instance { get; private set; }

		public void Init()
		{
			if (!Instance)
			{
				Instance = this;
			}
			InitializeHandlers();
			_isInitialized = true;
		}

		private void InitializeHandlers()
		{
			_slowHandler = new StatusHandler(SlowEffectPrefab, slowTransform, 1, StackMode.HighestPriority, delegate(BaseEnemyController enemy, float val)
			{
				enemy.status.SetSlowStat(val);
			}, delegate(BaseEnemyController enemy)
			{
				enemy.status.RemoveSlow();
			});
			_weakHandler = new StatusHandler(WeakEffectPrefab, weakTransform, 1, StackMode.HighestPriority, delegate(BaseEnemyController enemy, float val)
			{
				enemy.status.SetWeakStat(val);
			}, delegate(BaseEnemyController enemy)
			{
				enemy.status.RemoveWeak();
			});
			_burnHandler = new StatusHandler(BurnEffectPrefab, burnTransform, 1, StackMode.HighestPriority, null, delegate(BaseEnemyController enemy)
			{
				enemy.status.RemoveBurn();
			}, delegate(BaseEnemyController enemy, int damage)
			{
				enemy.Damage(damage, DamageType.Fire);
			});
			_poisonHandler = new StatusHandler(PoisonEffectPrefab, poisonTransform, 1, StackMode.HighestPriority, null, delegate(BaseEnemyController enemy)
			{
				enemy.status.RemovePoison();
			}, delegate(BaseEnemyController enemy, int damage)
			{
				enemy.Damage(damage, DamageType.Poison);
			});
			_bleedHandler = new StatusHandler(BleedEffectPrefab, bleedTransform, 10, StackMode.Add, null, delegate(BaseEnemyController enemy)
			{
				enemy.status.RemoveBleed();
			}, delegate(BaseEnemyController enemy, int damage)
			{
				enemy.Damage(damage, DamageType.Bleed);
			});
		}

		private void Update()
		{
			if (_isInitialized)
			{
				float time = Time.time;
				_slowHandler.Update(time);
				_burnHandler.Update(time);
				_poisonHandler.Update(time);
				_bleedHandler.Update(time);
				_weakHandler.Update(time);
			}
		}

		public void TransferStatus(BaseEnemyController source, BaseEnemyController target)
		{
			_slowHandler.TransferEffect(source, target);
			_burnHandler.TransferEffect(source, target);
			_poisonHandler.TransferEffect(source, target);
			_bleedHandler.TransferEffect(source, target);
			_weakHandler.TransferEffect(source, target);
		}

		public void RegisterSlowStatus(BaseEnemyController enemy, EnemyStatusData data)
		{
			_slowHandler.Register(enemy, data);
		}

		public void UnRegisterSlowStatus(BaseEnemyController enemy)
		{
			_slowHandler.UnRegister(enemy);
		}

		public void RegisterBurnStatus(BaseEnemyController enemy, EnemyStatusData data)
		{
			_burnHandler.Register(enemy, data);
		}

		public void UnRegisterBurnStatus(BaseEnemyController enemy)
		{
			_burnHandler.UnRegister(enemy);
		}

		public void RegisterPoisonStatus(BaseEnemyController enemy, EnemyStatusData data)
		{
			_poisonHandler.Register(enemy, data);
		}

		public void UnRegisterPoisonStatus(BaseEnemyController enemy)
		{
			_poisonHandler.UnRegister(enemy);
		}

		public void RegisterBleedStatus(BaseEnemyController enemy, EnemyStatusData data)
		{
			_bleedHandler.Register(enemy, data);
		}

		public void UnRegisterBleedStatus(BaseEnemyController enemy)
		{
			_bleedHandler.UnRegister(enemy);
		}

		public void RegisterWeakStatus(BaseEnemyController enemy, EnemyStatusData data)
		{
			_weakHandler.Register(enemy, data);
		}

		public void UnRegisterWeakStatus(BaseEnemyController enemy)
		{
			_weakHandler.UnRegister(enemy);
		}

		public void ConsumeStack(EnemyStatusID id, BaseEnemyController enemy)
		{
			switch (id)
			{
			case EnemyStatusID.Slow:
				_slowHandler.ConsumeStack(enemy);
				break;
			case EnemyStatusID.Burn:
				_burnHandler.ConsumeStack(enemy);
				break;
			case EnemyStatusID.Poison:
				_poisonHandler.ConsumeStack(enemy);
				break;
			case EnemyStatusID.Bleed:
				_bleedHandler.ConsumeStack(enemy);
				break;
			case EnemyStatusID.Weaken:
				_weakHandler.ConsumeStack(enemy);
				break;
			}
		}
	}
}
