using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Helpers;
using UnityEngine;

namespace AstralShift.HellMaiden.AI
{
	public class EnemyAIAttractor : MonoBehaviour
	{
		[Header("Attractor Settings")]
		[SerializeField]
		private float _attractionRadius = 5f;

		[SerializeField]
		private float _updateInterval = 0.1f;

		[Header("Visualization")]
		[SerializeField]
		private bool _showGizmos = true;

		[SerializeField]
		private Color _gizmoColor = Color.cyan;

		[Header("Combat Settings")]
		[SerializeField]
		private float hp_denominator = 1f;

		private float _lastUpdateTime;

		private Collider2D _collider;

		private bool _isAlive = true;

		private readonly List<BaseEnemyController> _enemiesInRangeCache = new List<BaseEnemyController>();

		private readonly List<EnemyController> _affectedEnemies = new List<EnemyController>();

		private readonly Dictionary<int, Transform> _enemyPreviousTargets = new Dictionary<int, Transform>();

		private readonly Dictionary<int, Action> _enemyDisposeHandlers = new Dictionary<int, Action>();

		private readonly HashSet<int> _disposedEnemies = new HashSet<int>();

		private float _sqrAttractionRadius;

		public Action OnEnemyAIAttractorDestroyed;

		private Action _onKilledHandler;

		private void Update()
		{
			if (_isAlive && Time.time - _lastUpdateTime >= _updateInterval)
			{
				UpdateAffectedEnemies();
				_lastUpdateTime = Time.time;
			}
		}

		private void OnDestroy()
		{
			ClearAllAffectedEnemies();
		}

		private void OnDisable()
		{
			ClearAllAffectedEnemies();
		}

		private void OnDrawGizmos()
		{
			if (!_showGizmos)
			{
				return;
			}
			Gizmos.color = _gizmoColor;
			Gizmos.DrawWireSphere(base.transform.position, _attractionRadius);
			if (!Application.isPlaying || _affectedEnemies == null)
			{
				return;
			}
			Gizmos.color = Color.green;
			foreach (EnemyController affectedEnemy in _affectedEnemies)
			{
				if (affectedEnemy != null)
				{
					Gizmos.DrawLine(base.transform.position, affectedEnemy.transform.position);
				}
			}
		}

		public void Initialize()
		{
			_isAlive = true;
			UpdateSqrRadius();
			EnemyDamageableObject componentInChildren = GetComponentInChildren<EnemyDamageableObject>();
			componentInChildren.MaxHealth = (float)GameDirector.Instance.Player.PlayerStats.MaxHP / hp_denominator;
			componentInChildren.ReviveObject();
			componentInChildren.OnKilled = delegate
			{
				Die();
			};
		}

		public void ClearAllAffectedEnemies()
		{
			foreach (EnemyController affectedEnemy in _affectedEnemies)
			{
				if (!(affectedEnemy == null))
				{
					int instanceID = affectedEnemy.GetInstanceID();
					if (_enemyPreviousTargets.TryGetValue(instanceID, out var value) && affectedEnemy.Target == base.transform)
					{
						affectedEnemy.Target = value;
					}
					if (_enemyDisposeHandlers.TryGetValue(instanceID, out var value2))
					{
						affectedEnemy.OnDispose -= value2;
					}
				}
			}
			_affectedEnemies.Clear();
			_enemyPreviousTargets.Clear();
			_enemyDisposeHandlers.Clear();
			_disposedEnemies.Clear();
		}

		private void Die(Collider2D other = null)
		{
			if (_isAlive)
			{
				_isAlive = false;
				OnEnemyAIAttractorDestroyed?.Invoke();
				OnEnemyAIAttractorDestroyed = null;
				ClearAllAffectedEnemies();
				UnityEngine.Object.Destroy(base.gameObject);
			}
		}

		private void UpdateSqrRadius()
		{
			_sqrAttractionRadius = _attractionRadius * _attractionRadius;
		}

		private void UpdateAffectedEnemies()
		{
			for (int num = _affectedEnemies.Count - 1; num >= 0; num--)
			{
				EnemyController enemyController = _affectedEnemies[num];
				if (enemyController == null)
				{
					_affectedEnemies.RemoveAt(num);
				}
				else
				{
					int instanceID = enemyController.GetInstanceID();
					if (_disposedEnemies.Contains(instanceID))
					{
						_affectedEnemies.RemoveAt(num);
					}
					else if (!IsEnemyInRange(enemyController))
					{
						RestoreEnemyTarget(enemyController, instanceID);
						_affectedEnemies.RemoveAt(num);
					}
				}
			}
			AIHelpers.FindEnemiesInCircleRangeNonAlloc(base.transform.position, 0f, _attractionRadius, _enemiesInRangeCache);
			foreach (BaseEnemyController item in _enemiesInRangeCache)
			{
				if (!(item is EnemyController { isActiveAndEnabled: not false } enemyController2))
				{
					continue;
				}
				int instanceID2 = enemyController2.GetInstanceID();
				if (_disposedEnemies.Contains(instanceID2))
				{
					continue;
				}
				bool flag = false;
				for (int i = 0; i < _affectedEnemies.Count; i++)
				{
					EnemyController enemyController3 = _affectedEnemies[i];
					if (enemyController3 != null && enemyController3.GetInstanceID() == instanceID2)
					{
						flag = true;
						break;
					}
				}
				if (!flag)
				{
					_affectedEnemies.Add(enemyController2);
					SubscribeToEnemyDispose(enemyController2, instanceID2);
					RedirectEnemyTarget(enemyController2, instanceID2);
				}
			}
		}

		private bool IsEnemyInRange(EnemyController enemy)
		{
			if (enemy == null)
			{
				return false;
			}
			return (base.transform.position - enemy.transform.position).sqrMagnitude <= _sqrAttractionRadius;
		}

		private void SubscribeToEnemyDispose(EnemyController enemy, int enemyId)
		{
			if (!(enemy == null) && !_enemyDisposeHandlers.ContainsKey(enemyId))
			{
				Action value = delegate
				{
					OnEnemyDisposed(enemy, enemyId);
				};
				_enemyDisposeHandlers[enemyId] = value;
				enemy.OnDispose += value;
			}
		}

		private void OnEnemyDisposed(EnemyController disposedEnemy, int enemyId)
		{
			if (disposedEnemy == null)
			{
				return;
			}
			_disposedEnemies.Add(enemyId);
			for (int num = _affectedEnemies.Count - 1; num >= 0; num--)
			{
				EnemyController enemyController = _affectedEnemies[num];
				if ((object)enemyController != null && enemyController.GetInstanceID() == enemyId)
				{
					_affectedEnemies.RemoveAt(num);
					break;
				}
			}
			_enemyPreviousTargets.Remove(enemyId);
			_enemyDisposeHandlers.Remove(enemyId);
		}

		private void RedirectEnemyTarget(EnemyController enemy, int enemyId)
		{
			if (!(enemy == null))
			{
				if (!_enemyPreviousTargets.ContainsKey(enemyId))
				{
					_enemyPreviousTargets[enemyId] = enemy.Target;
				}
				enemy.Target = base.transform;
			}
		}

		private void RestoreEnemyTarget(EnemyController enemy, int enemyId)
		{
			if (enemy == null)
			{
				return;
			}
			if (_enemyPreviousTargets.TryGetValue(enemyId, out var value))
			{
				if (enemy.Target == base.transform)
				{
					enemy.Target = value;
				}
				_enemyPreviousTargets.Remove(enemyId);
			}
			if (_enemyDisposeHandlers.TryGetValue(enemyId, out var value2))
			{
				enemy.OnDispose -= value2;
				_enemyDisposeHandlers.Remove(enemyId);
			}
			_disposedEnemies.Remove(enemyId);
		}
	}
}
