using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Common;
using AstralShift.HellMaiden.Helpers;
using AstralShift.Helpers;
using AstralShift.Helpers.Attributes;
using AstralShift.Rendering;
using Com.LuisPedroFonseca.ProCamera2D;
using Unity.Collections;
using Unity.Profiling;
using UnityEngine;

namespace AstralShift.HellMaiden.AI
{
	public class EnemyAIManager : MonoBehaviour
	{
		private struct BoundsData
		{
			public Vector2 minBounds;

			public Vector2 maxBounds;

			public BoundsData(Bounds bounds)
			{
				minBounds = new Vector2(bounds.min.x, bounds.min.y);
				maxBounds = new Vector2(bounds.max.x, bounds.max.y);
			}
		}

		[Header("General Settings")]
		[SerializeField]
		[AstralShift.Helpers.Attributes.ReadOnly]
		private List<BaseEnemyController> _enemiesOnScreen;

		[SerializeField]
		[AstralShift.Helpers.Attributes.ReadOnly]
		private List<BaseEnemyController> _enemiesOffScreen;

		private Dictionary<int, BaseEnemyController> _allEnemiesLut;

		private Dictionary<int, EnemyController> _hordeEnemiesLut;

		private Dictionary<int, EnemySimulationAuthority> _simulationAuthorities;

		private List<int> _allEnemiesIDCache;

		private List<int> _hordeEnemiesIDCache;

		private Dictionary<int, bool> _visibleIDsLUT;

		[SerializeField]
		private ComputeShader _visibilityShader;

		private const int ComputeMaxEnemyCount = 1024;

		private Vector4 _cameraRectData;

		private ComputeBuffer _boundsBuffer;

		private NativeArray<BoundsData> _boundsDataArrayNativeCache;

		private Dictionary<int, int> _idToNativeIndexLut = new Dictionary<int, int>();

		private ComputeBuffer _instanceIDsBuffer;

		private ComputeBuffer _visibleEnemyIDsBuffer;

		private ComputeBuffer _visibleCountBuffer;

		private ComputeBuffer _dispatchArgsBuffer;

		private HashSet<int> _visibleIDTempHashSet = new HashSet<int>();

		private int _lastVisibleCount;

		private bool _pendingAsyncGPUReadback;

		private AsyncGPUReadbackQueue _asyncGPUReadbackQueue;

		private bool _visibilityResourcesInitialized;

		private const float EnemyCullingTimeout = 5f;

		private Dictionary<EnemyController, float> _rubberBandTracker;

		private Dictionary<int, float> _recentlyRegisteredEnemies;

		private const float RecentlyRegisteredEnemiesRubberBandTimeout = 30f;

		private List<EnemyController> _stuckHordeEnemies;

		public float stuckCheckTimeInterval;

		[Header("Rubberband Settings")]
		public LayerMask obstaclesLayerMask;

		public float offscreenTimeoutDistance = 1.5f;

		private int _currentIndex;

		private int _enemiesPerFrameCount;

		private int _remainingEnemiesToProcess;

		private int _totalEnemies;

		private int _perFrameMaxEnemies = 10;

		private Dictionary<int, int> _perFrameBatchCount = new Dictionary<int, int>();

		private WaitForSeconds _stuckCheckWaitInstance;

		private static readonly ProfilerMarker _computeDispatchAndReadbackEnqueueMarker = new ProfilerMarker("EnemyAIManager.ComputeDispatchAndReadbackEnqueue");

		private static readonly ProfilerMarker _computeCountReadback = new ProfilerMarker("EnemyAIManager.ComputeCountReadback");

		private static readonly ProfilerMarker _computeVisibleIDsReadback = new ProfilerMarker("EnemyAIManager.ComputeVisibleIDsReadback");

		private static readonly int CameraRect = Shader.PropertyToID("cameraRect");

		private static readonly int BoundsBuffer = Shader.PropertyToID("boundsBuffer");

		private static readonly int InstanceIDs = Shader.PropertyToID("instanceIDs");

		private static readonly int VisibleInstanceIDs = Shader.PropertyToID("visibleInstanceIDs");

		private const float RubberBandBiasPlayerVelThreshold = 0.1f;

		private const float RubberBandFrontAngleRange = 90f;

		private List<EnemyController> _deactivatedEnemies;

		public static EnemyAIManager Instance { get; private set; }

		public List<BaseEnemyController> EnemiesOnScreen => _enemiesOnScreen;

		public List<BaseEnemyController> EnemiesOffScreen => _enemiesOffScreen;

		public Dictionary<int, BaseEnemyController> AllEnemies => _allEnemiesLut;

		public Dictionary<int, EnemyController> HordeEnemies => _hordeEnemiesLut;

		public HashSet<int> VisibleIDTempHashSet => _visibleIDTempHashSet;

		private void Awake()
		{
			Instance = this;
			InitializeRuntimeCollections();
		}

		private void Start()
		{
			RunHordeEnemiesStuckCheck();
			if (!Application.isBatchMode &&
				SystemInfo.supportsComputeShaders &&
				_visibilityShader != null)
			{
				InitializeComputeBuffers();
			}
		}

		private void InitializeRuntimeCollections()
		{
			_perFrameBatchCount = new Dictionary<int, int>();
			_perFrameBatchCount.Add(0, 10);
			_perFrameBatchCount.Add(200, 25);
			_perFrameBatchCount.Add(500, 50);
			_allEnemiesLut = new Dictionary<int, BaseEnemyController>();
			_allEnemiesIDCache = new List<int>();
			_hordeEnemiesLut = new Dictionary<int, EnemyController>();
			_simulationAuthorities = new Dictionary<int, EnemySimulationAuthority>();
			_hordeEnemiesIDCache = new List<int>();
			_enemiesOnScreen = new List<BaseEnemyController>();
			_enemiesOffScreen = new List<BaseEnemyController>();
			_rubberBandTracker = new Dictionary<EnemyController, float>();
			_recentlyRegisteredEnemies = new Dictionary<int, float>();
			_visibleIDsLUT = new Dictionary<int, bool>();
		}

		private void InitializeComputeBuffers()
		{
			_asyncGPUReadbackQueue = new AsyncGPUReadbackQueue(this);
			_cameraRectData = default(Vector4);
			_boundsDataArrayNativeCache = new NativeArray<BoundsData>(1024, Allocator.Persistent);
			_boundsBuffer = new ComputeBuffer(1024, 16);
			_instanceIDsBuffer = new ComputeBuffer(1024, 4);
			_visibleEnemyIDsBuffer = new ComputeBuffer(1024, 4, ComputeBufferType.Append);
			_visibleCountBuffer = new ComputeBuffer(1, 4, ComputeBufferType.Raw);
			_dispatchArgsBuffer = new ComputeBuffer(3, 12, ComputeBufferType.IndirectArguments);
			_visibilityResourcesInitialized = true;
		}

		private void Dispose()
		{
			if (_boundsDataArrayNativeCache.IsCreated)
			{
				_boundsDataArrayNativeCache.Dispose();
			}
			_boundsBuffer?.Release();
			_instanceIDsBuffer?.Release();
			_visibleEnemyIDsBuffer?.Release();
			_visibleCountBuffer?.Release();
			_dispatchArgsBuffer?.Release();
			_asyncGPUReadbackQueue?.Clear();
			_visibilityResourcesInitialized = false;
			_lastVisibleCount = 0;
			_pendingAsyncGPUReadback = false;
			_currentIndex = 0;
		}

		private void OnDestroy()
		{
			Dispose();
			Instance = null;
		}

		public void RegisterEnemy(BaseEnemyController enemy)
		{
			int instanceID = enemy.GetInstanceID();
			if (!_allEnemiesLut.ContainsKey(instanceID))
			{
				int count = _allEnemiesIDCache.Count;
				_idToNativeIndexLut[instanceID] = count;
				_allEnemiesIDCache.Add(instanceID);
				if (_visibilityResourcesInitialized && count < ComputeMaxEnemyCount)
				{
					_boundsDataArrayNativeCache[count] = new BoundsData(enemy.Bounds);
				}
				_allEnemiesLut.TryAdd(instanceID, enemy);
				if (enemy.TryGetComponent(out EnemySimulationAuthority authority))
				{
					_simulationAuthorities[instanceID] = authority;
				}
				if (enemy is EnemyController enemy2)
				{
					_recentlyRegisteredEnemies.TryAdd(instanceID, Time.time);
					_visibleIDsLUT.Add(instanceID, value: false);
					RegisterHordeEnemy(enemy2);
					UpdateEnemyDestination(instanceID);
				}
			}
		}

		public void UnRegisterEnemy(BaseEnemyController enemy)
		{
			int instanceID = enemy.GetInstanceID();
			if (_allEnemiesLut == null || !_allEnemiesLut.ContainsKey(instanceID))
			{
				return;
			}
			if (_idToNativeIndexLut.TryGetValue(instanceID, out var value))
			{
				int num = _allEnemiesIDCache.Count - 1;
				if (value != num)
				{
					int num2 = _allEnemiesIDCache[num];
					_allEnemiesIDCache[value] = num2;
					_idToNativeIndexLut[num2] = value;
					// Batch/headless players intentionally skip GPU visibility setup,
					// so the NativeArray is not created there. Unregistration is still
					// required for the gameplay lookup tables, but there is no native
					// visibility slot to compact in that configuration.
					if (_boundsDataArrayNativeCache.IsCreated &&
						value >= 0 && value < _boundsDataArrayNativeCache.Length &&
						num >= 0 && num < _boundsDataArrayNativeCache.Length)
					{
						_boundsDataArrayNativeCache[value] =
							_boundsDataArrayNativeCache[num];
					}
				}
				_allEnemiesIDCache.RemoveAt(num);
				_idToNativeIndexLut.Remove(instanceID);
			}
			_allEnemiesLut.Remove(instanceID);
			_simulationAuthorities.Remove(instanceID);
			if (enemy is EnemyController enemy2)
			{
				_recentlyRegisteredEnemies.Remove(instanceID);
				_visibleIDsLUT.Remove(instanceID);
				RemoveFromRubberBandTracker(instanceID);
				UnRegisterHordeEnemy(enemy2);
			}
			if (_currentIndex >= _allEnemiesIDCache.Count)
			{
				_currentIndex = Mathf.Clamp(_allEnemiesIDCache.Count - 1, 0, _allEnemiesIDCache.Count);
			}
		}

		private void RegisterHordeEnemy(EnemyController enemy)
		{
			int instanceID = enemy.GetInstanceID();
			if (!_hordeEnemiesIDCache.Contains(instanceID))
			{
				_hordeEnemiesIDCache.Add(instanceID);
			}
			_hordeEnemiesLut.TryAdd(instanceID, enemy);
		}

		private void UnRegisterHordeEnemy(EnemyController enemy)
		{
			int instanceID = enemy.GetInstanceID();
			if (_hordeEnemiesIDCache.Contains(instanceID))
			{
				_hordeEnemiesIDCache.Remove(instanceID);
			}
			_hordeEnemiesLut.Remove(instanceID);
			UnRegisterStuckHordeEnemy(enemy);
		}

		public void DisposeHordeEnemies()
		{
			for (int num = _hordeEnemiesIDCache.Count - 1; num >= 0; num--)
			{
				int key = _hordeEnemiesIDCache[num];
				BaseEnemyController baseEnemyController = _hordeEnemiesLut[key];
				UnRegisterEnemy(baseEnemyController);
				Object.Destroy(baseEnemyController);
			}
		}

		private void Update()
		{
			if (Time.timeScale == 0f || _allEnemiesIDCache.Count == 0)
			{
				return;
			}
			EvaluatePerFrameBatchCount();
			int count = _allEnemiesIDCache.Count;
			int num = Mathf.Min(_perFrameMaxEnemies, (_remainingEnemiesToProcess > 0) ? _remainingEnemiesToProcess : count);
			if (_remainingEnemiesToProcess <= 0)
			{
				_remainingEnemiesToProcess = count;
			}
			int num2 = 0;
			int num3 = 0;
			while (num2 < num && _remainingEnemiesToProcess > 0 && num3 < count)
			{
				num3++;
				if (_currentIndex >= _allEnemiesIDCache.Count)
				{
					_currentIndex = 0;
				}
				int num4 = _allEnemiesIDCache[_currentIndex];
				if ((bool)_allEnemiesLut[num4] && _allEnemiesLut[num4].isActiveAndEnabled)
				{
					UpdateEnemySimulation(num4);
					if (AllowsRubberBand(num4))
					{
						EvaluateAndTryRubberBand(num4);
					}
					num2++;
				}
				_currentIndex++;
				_remainingEnemiesToProcess--;
			}
			if (_remainingEnemiesToProcess <= 0)
			{
				_currentIndex = 0;
			}
		}

		private void EvaluatePerFrameBatchCount()
		{
			foreach (KeyValuePair<int, int> item in _perFrameBatchCount.Where((KeyValuePair<int, int> keyValuePair) => _allEnemiesIDCache.Count > keyValuePair.Key))
			{
				_perFrameMaxEnemies = item.Value;
			}
		}

		private void FixedUpdate()
		{
			for (int num = _allEnemiesIDCache.Count - 1; num >= 0; num--)
			{
				int num2 = _allEnemiesIDCache[num];
				if (_allEnemiesLut[num2].isActiveAndEnabled)
				{
					if (!_hordeEnemiesLut.TryGetValue(num2, out var value))
					{
						UpdateEnemyBoundsInNativeCache(num2);
						break;
					}
					if (!AllowsNavigation(num2))
					{
						UpdateEnemyBoundsInNativeCache(num2);
						continue;
					}
					if (_visibleIDsLUT.TryGetValue(num2, out var value2) && value2 && value.usesPathfinding)
					{
						value.CheckIfStuck();
					}
					value.RunFixedUpdate();
					UpdateEnemyBoundsInNativeCache(num2);
				}
			}
		}

		private void UpdateEnemyBoundsInNativeCache(int id)
		{
			if (_visibilityResourcesInitialized &&
				_idToNativeIndexLut.TryGetValue(id, out var value) &&
				value < ComputeMaxEnemyCount)
			{
				_boundsDataArrayNativeCache[value] = new BoundsData(_allEnemiesLut[id].Bounds);
			}
		}

		private void LateUpdate()
		{
			for (int num = _hordeEnemiesIDCache.Count - 1; num >= 0; num--)
			{
				int key = _hordeEnemiesIDCache[num];
				if (_hordeEnemiesLut[key].isActiveAndEnabled && AllowsCombatDecisions(key))
				{
					_hordeEnemiesLut[key].RunLateUpdate();
				}
			}
			using (_computeDispatchAndReadbackEnqueueMarker.Auto())
			{
				CheckEnemiesVisibility();
			}
		}

		private void UpdateEnemyDestination(int id)
		{
			if (_hordeEnemiesLut.ContainsKey(id) && AllowsNavigation(id))
			{
				_hordeEnemiesLut[id].UpdateDestination();
			}
		}

		private void UpdateEnemySimulation(int id)
		{
			UpdateEnemyDestination(id);
			if (_hordeEnemiesLut.TryGetValue(id, out EnemyController enemy) &&
				AllowsCombatDecisions(id))
			{
				enemy.RunUpdate();
			}
		}

		private void CheckEnemiesVisibility()
		{
			if (!_visibilityResourcesInitialized ||
				_pendingAsyncGPUReadback ||
				!ProCamera2D.Exists)
			{
				return;
			}
			_pendingAsyncGPUReadback = true;
			UpdateCameraRect();
			UpdateBoundsData();
			if (_visibleEnemyIDsBuffer == null)
			{
				_pendingAsyncGPUReadback = false;
				return;
			}
			VisibilityComputeUtils.GetCompatibilityKernelAndGroups(_allEnemiesIDCache.Count, out var kernelIndex, out var groupsX);
			uint[] data = new uint[3]
			{
				(uint)groupsX,
				1u,
				1u
			};
			_dispatchArgsBuffer.SetData(data);
			_visibilityShader.SetVector(CameraRect, _cameraRectData);
			_visibilityShader.SetBuffer(kernelIndex, BoundsBuffer, _boundsBuffer);
			_visibilityShader.SetBuffer(kernelIndex, InstanceIDs, _instanceIDsBuffer);
			_visibilityShader.SetBuffer(kernelIndex, VisibleInstanceIDs, _visibleEnemyIDsBuffer);
			_visibleEnemyIDsBuffer.SetCounterValue(0u);
			_visibilityShader.DispatchIndirect(kernelIndex, _dispatchArgsBuffer);
			ComputeBuffer.CopyCount(_visibleEnemyIDsBuffer, _visibleCountBuffer, 0);
			_asyncGPUReadbackQueue.EnqueueRequest(_visibleCountBuffer, delegate(NativeArray<int> countBufferResult)
			{
				using (_computeCountReadback.Auto())
				{
					_lastVisibleCount = countBufferResult[0];
					if (_lastVisibleCount == 0)
					{
						UpdateVisibilityLists(default(NativeSlice<int>));
						_pendingAsyncGPUReadback = false;
					}
					else
					{
						_asyncGPUReadbackQueue.EnqueueRequest(_visibleEnemyIDsBuffer, _lastVisibleCount, delegate(NativeArray<int> enemyIDsBufferResult)
						{
							using (_computeVisibleIDsReadback.Auto())
							{
								if (enemyIDsBufferResult.Length == 0)
								{
									_pendingAsyncGPUReadback = false;
								}
								else
								{
									NativeSlice<int> visibleIDs = enemyIDsBufferResult.Slice(0, _lastVisibleCount);
									UpdateVisibilityLists(visibleIDs);
									_pendingAsyncGPUReadback = false;
								}
							}
						}, delegate
						{
							_pendingAsyncGPUReadback = false;
						});
					}
				}
			}, delegate
			{
				_pendingAsyncGPUReadback = false;
			});
		}

		private void UpdateCameraRect()
		{
			if ((bool)ProCamera2D.Instance && (bool)ProCamera2D.Instance.GameCamera)
			{
				Vector3 position = ProCamera2D.Instance.GameCamera.transform.position;
				float num = ProCamera2D.Instance.GameCamera.orthographicSize * 2f;
				float num2 = num * ProCamera2D.Instance.GameCamera.aspect;
				_cameraRectData = new Vector4(position.x - num2 * 0.5f, position.x + num2 * 0.5f, position.y - num * 0.5f, position.y + num * 0.5f);
			}
		}

		private void UpdateBoundsData()
		{
			if (!_visibilityResourcesInitialized)
			{
				return;
			}
			int count = _allEnemiesIDCache.Count;
			if (count != 0)
			{
				int count2 = Mathf.Min(count, 1024);
				_instanceIDsBuffer.SetData(_allEnemiesIDCache, 0, 0, count2);
				_boundsBuffer.SetData(_boundsDataArrayNativeCache, 0, 0, count2);
			}
		}

		private void UpdateVisibilityLists(NativeSlice<int> visibleIDs)
		{
			_visibleIDTempHashSet.Clear();
			foreach (int item in visibleIDs)
			{
				_visibleIDTempHashSet.Add(item);
			}
			_enemiesOnScreen.Clear();
			_enemiesOffScreen.Clear();
			foreach (int item2 in _allEnemiesIDCache)
			{
				if (!_allEnemiesLut.TryGetValue(item2, out var value) || !value)
				{
					continue;
				}
				if (_visibleIDTempHashSet.Contains(item2))
				{
					_recentlyRegisteredEnemies.Remove(item2);
					_enemiesOnScreen.Add(value);
					_visibleIDsLUT[item2] = true;
					RemoveFromRubberBandTracker(item2);
					continue;
				}
				_enemiesOffScreen.Add(value);
				_visibleIDsLUT[item2] = false;
				if (value.CanRubberband)
				{
					AddToRubberBandTracker(item2);
				}
			}
		}

		private void EvaluateAndTryRubberBand(int id)
		{
			if (!_hordeEnemiesLut.TryGetValue(id, out var value) || !_rubberBandTracker.TryGetValue(value, out var value2))
			{
				return;
			}
			if (!value.CanRubberband)
			{
				RemoveFromRubberBandTracker(id);
				return;
			}
			bool num = ProCamera2DHelpers.GetDistanceToCameraExtentsNonAlloc(value.Transform.position) >= value.RubberbandMaxDistance;
			bool flag = Time.time - value2 >= 5f;
			if (num || flag)
			{
				RemoveFromRubberBandTracker(id);
				if (value.endTime != 0f && value.endTime <= ProgressionManager.Instance.CurrentTime)
				{
					value.Kill(instant: true, dropXp: false);
				}
				else
				{
					RubberBandHordeEnemyPosition(value);
				}
			}
		}

		private void AddToRubberBandTracker(int id)
		{
			if (!_hordeEnemiesLut.TryGetValue(id, out var value))
			{
				return;
			}
			if (_recentlyRegisteredEnemies.TryGetValue(id, out var value2))
			{
				if (!(Time.time - value2 > 30f))
				{
					return;
				}
				_recentlyRegisteredEnemies.Remove(id);
			}
			if (value.allowRubberband)
			{
				_rubberBandTracker.TryAdd(value, Time.time);
			}
		}

		private void RemoveFromRubberBandTracker(int id)
		{
			if (_hordeEnemiesLut.TryGetValue(id, out var value))
			{
				_rubberBandTracker.Remove(value);
			}
		}

		public void RubberBandHordeEnemyPosition(EnemyController enemy)
		{
			Rigidbody2D targetBody = enemy.Target != null
				? enemy.Target.GetComponentInParent<Rigidbody2D>()
				: null;
			Vector2 linearVelocity = targetBody != null
				? targetBody.linearVelocity
				: Vector2.zero;
			Vector2 spawnPosition = default(Vector2);
			if (enemy.direction != Direction.None)
			{
				SpawnHelpers.GetOffScreenSpawnPositionInDirection(enemy.spawnReferenceRadius, enemy.Bounds, offscreenTimeoutDistance, obstaclesLayerMask, enemy.direction.ToVector2(), out spawnPosition, enemy.angle);
			}
			else if (linearVelocity.magnitude < 0.1f)
			{
				SpawnHelpers.GetOffScreenSpawnPosition(enemy.spawnReferenceRadius, enemy.Bounds, offscreenTimeoutDistance, obstaclesLayerMask, out spawnPosition);
			}
			else
			{
				SpawnHelpers.GetOffScreenSpawnPositionInDirection(enemy.spawnReferenceRadius, enemy.Bounds, offscreenTimeoutDistance, obstaclesLayerMask, linearVelocity, out spawnPosition);
			}
			enemy.transform.position = spawnPosition;
			if (_simulationAuthorities.TryGetValue(
				enemy.GetInstanceID(),
				out EnemySimulationAuthority authority))
			{
				authority.MarkDiscontinuity();
			}
			if (_visibilityResourcesInitialized &&
				_idToNativeIndexLut.TryGetValue(enemy.GetInstanceID(), out var value) &&
				value < ComputeMaxEnemyCount)
			{
				_boundsDataArrayNativeCache[value] = new BoundsData(enemy.Bounds);
			}
			if (enemy.rubberbandStatsReset)
			{
				enemy.ResetEnemyCondition();
			}
		}

		private void RunHordeEnemiesStuckCheck()
		{
			_stuckHordeEnemies = new List<EnemyController>();
			_stuckCheckWaitInstance = new WaitForSeconds(stuckCheckTimeInterval);
			StartCoroutine(EnemiesStuckCheck());
		}

		private IEnumerator EnemiesStuckCheck()
		{
			while (true)
			{
				if (_stuckHordeEnemies.Count == 0)
				{
					yield return null;
				}
				else
				{
					int i = _stuckHordeEnemies.Count - 1;
					while (i >= 0 && i < _stuckHordeEnemies.Count && _stuckHordeEnemies.Count != 0)
					{
						if (_stuckHordeEnemies[i] == null)
						{
							_stuckHordeEnemies.RemoveAt(i);
						}
						else
						{
							if (!AllowsNavigation(_stuckHordeEnemies[i].GetInstanceID()))
							{
								UnRegisterStuckHordeEnemy(_stuckHordeEnemies[i]);
								i--;
								continue;
							}
							bool num = _stuckHordeEnemies[i].UnStuckCheck();
							_stuckHordeEnemies[i].RefreshMovementMethod();
							if (!num)
							{
								UnRegisterStuckHordeEnemy(_stuckHordeEnemies[i]);
							}
							yield return null;
						}
						i--;
					}
				}
				yield return _stuckCheckWaitInstance;
			}
		}

		public void RegisterStuckHordeEnemy(EnemyController enemy)
		{
			if (!_stuckHordeEnemies.Contains(enemy))
			{
				_stuckHordeEnemies.Add(enemy);
			}
		}

		public void UnRegisterStuckHordeEnemy(EnemyController enemy)
		{
			if (_stuckHordeEnemies.Contains(enemy))
			{
				_stuckHordeEnemies.Remove(enemy);
			}
		}

		public void DeactivateAllEnemies()
		{
			_deactivatedEnemies = new List<EnemyController>();
			foreach (EnemyController value in HordeEnemies.Values)
			{
				_deactivatedEnemies.Add(value);
				value.Deactivate();
			}
		}

		public void ActivateAllEnemies()
		{
			foreach (EnemyController deactivatedEnemy in _deactivatedEnemies)
			{
				RubberBandHordeEnemyPosition(deactivatedEnemy);
				deactivatedEnemy.Activate();
			}
		}

		private bool AllowsNavigation(int instanceID)
		{
			return !_simulationAuthorities.TryGetValue(
				instanceID,
				out EnemySimulationAuthority authority) ||
				authority.RunsNavigation;
		}

		private bool AllowsCombatDecisions(int instanceID)
		{
			return !_simulationAuthorities.TryGetValue(
				instanceID,
				out EnemySimulationAuthority authority) ||
				authority.RunsCombatDecisions;
		}

		private bool AllowsRubberBand(int instanceID)
		{
			return !_simulationAuthorities.TryGetValue(
				instanceID,
				out EnemySimulationAuthority authority) ||
				authority.RunsRubberBand;
		}
	}
}
