using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AstralShift.HellMaiden.Combat;
using AstralShift.Pooling;
using FMODUnity;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Boss.Minos
{
	public class TailCoilAttackBehaviour : BossAttackBehaviour
	{
		[Header("Settings")]
		[SerializeField]
		protected MinosMovementController movementController;

		public AnimatedBossAttack attackPrefab;

		private GenericPooler<AnimatedBossAttack> _pooler;

		[SerializeField]
		protected Vector2Int numberOfAttacks = new Vector2Int(3, 4);

		[SerializeField]
		protected int attacksBetweenDistance;

		[SerializeField]
		protected float startTimeout = 0.5f;

		[SerializeField]
		protected Vector2Int disposeTimeout = new Vector2Int(10, 12);

		private List<AnimatedBossAttack> _spawnedAttacks;

		private List<AnimatedBossAttack> _activeAttacks;

		private Dictionary<AnimatedBossAttack, Vector3> _unavailablePositions;

		private Dictionary<AnimatedBossAttack, float> _attackSpawnTimestamps;

		private Coroutine _launchAttackCoroutine;

		private Coroutine _trackAttackCoroutine;

		private Vector3[] _availablePositions;

		[Header("Sound")]
		public EventReference tailEventRef;

		public Rect samplingArea;

		public PolygonCollider2D polyCollider;

		private const float Spacing = 0.75f;

		private List<Vector3> insidePoints = new List<Vector3>();

		public override void Init(BossController controller)
		{
			base.Init(controller);
			_spawnedAttacks = new List<AnimatedBossAttack>();
			_activeAttacks = new List<AnimatedBossAttack>();
			_attackSpawnTimestamps = new Dictionary<AnimatedBossAttack, float>();
			_unavailablePositions = new Dictionary<AnimatedBossAttack, Vector3>();
			_availablePositions = GeneratePointsInGrid();
		}

		public override void Positioning()
		{
			bossController.Movement.StopMovement();
			onPositioningEnd?.Invoke();
		}

		public override void Warning()
		{
			BarkWarning();
			WarningBossAnimation(onWarningEnd);
		}

		public override void Attack()
		{
			AttackBossAnimation(null);
			SpawnAttack();
		}

		public virtual void SpawnAttack()
		{
			if (_pooler == null)
			{
				_pooler = PoolManager.Instance.GetOrCreatePooler(attackPrefab);
			}
			StartCoroutine(SpawnAttackCoroutine());
			if (_trackAttackCoroutine != null)
			{
				StopCoroutine(_trackAttackCoroutine);
				if (_spawnedAttacks.Count != 0)
				{
					for (int num = _spawnedAttacks.Count - 1; num >= 0; num--)
					{
						Dispose(_spawnedAttacks[num]);
					}
				}
			}
			_trackAttackCoroutine = StartCoroutine(TrackAttackTimeoutCoroutine());
			onAttackEnd?.Invoke();
		}

		public override void Dispose()
		{
			if (_spawnedAttacks != null)
			{
				StopAllCoroutines();
				for (int i = 0; i < _spawnedAttacks.Count; i++)
				{
					UnityEngine.Object.Destroy(_spawnedAttacks[i].gameObject);
				}
				_spawnedAttacks.Clear();
			}
		}

		protected virtual IEnumerator SpawnAttackCoroutine()
		{
			if (_pooler == null)
			{
				_pooler = PoolManager.Instance.GetOrCreatePooler(attackPrefab);
			}
			WaitForSeconds waitForSeconds = new WaitForSeconds(startTimeout);
			int count = UnityEngine.Random.Range(numberOfAttacks.x, numberOfAttacks.y + 1);
			List<Vector3> positions = new List<Vector3>();
			if (!GetAvailablePositions(count, attacksBetweenDistance, out positions))
			{
				yield break;
			}
			for (int i = 0; i < positions.Count; i++)
			{
				AnimatedBossAttack attack = _pooler.GetOrCreate(null, activate: true);
				attack.transform.position = new Vector3(positions[i].x, positions[i].y, positions[i].y + 100f);
				RuntimeManager.PlayOneShot(tailEventRef, positions[i]);
				_unavailablePositions.TryAdd(attack, positions[i]);
				Action onEnd = delegate
				{
					_spawnedAttacks.Add(attack);
					_attackSpawnTimestamps.TryAdd(attack, Time.time);
				};
				attack.RunInAnimation(onEnd);
				yield return waitForSeconds;
			}
		}

		public IEnumerator TrackAttackTimeoutCoroutine()
		{
			float currentTimeout = UnityEngine.Random.Range(disposeTimeout.x, disposeTimeout.y);
			float startTime = Time.time;
			while (Time.time - 4f < currentTimeout + startTime)
			{
				if (_spawnedAttacks.Count != 0)
				{
					for (int num = _spawnedAttacks.Count - 1; num >= 0; num--)
					{
						TryAttackIfInRange(_spawnedAttacks[num], 120f, 5f);
						if (!_activeAttacks.Contains(_spawnedAttacks[num]) && _attackSpawnTimestamps.TryGetValue(_spawnedAttacks[num], out var value) && Time.time - value > currentTimeout)
						{
							Dispose(_spawnedAttacks[num]);
						}
					}
				}
				yield return null;
			}
		}

		private void TryAttackIfInRange(AnimatedBossAttack attack, float angle, float maxDistance)
		{
			if (!_activeAttacks.Contains(attack))
			{
				attack.RunHitAnimation(ReturnToPreviousState);
				_activeAttacks.Add(attack);
			}
			void ReturnToPreviousState()
			{
				_activeAttacks.Remove(attack);
				Dispose(attack);
			}
		}

		private void Dispose(AnimatedBossAttack attack)
		{
			attack.RunOutAnimation(ReturnToPool);
			_attackSpawnTimestamps.Remove(attack);
			_spawnedAttacks.Remove(attack);
			void ReturnToPool()
			{
				_unavailablePositions.Remove(attack);
				_pooler.Return(attack);
			}
		}

		public Vector3[] GeneratePointsInGrid()
		{
			insidePoints.Clear();
			for (float num = samplingArea.xMin; num <= samplingArea.xMax; num += 0.75f)
			{
				for (float num2 = samplingArea.yMin; num2 <= samplingArea.yMax; num2 += 0.75f)
				{
					Vector3 vector = new Vector3(num, num2, 0f);
					if (polyCollider.OverlapPoint(vector))
					{
						insidePoints.Add(vector);
					}
				}
			}
			return insidePoints.ToArray();
		}

		public bool GetAvailablePositions(int count, float radius, out List<Vector3> positions)
		{
			positions = new List<Vector3>();
			List<Vector3> filteredPositions = (from p in _availablePositions
				where !_unavailablePositions.Values.Contains(p)
				where Vector2.Distance(p, movementController.Position) > radius
				select p).ToList();
			if (filteredPositions.Count == 0)
			{
				return false;
			}
			List<Vector3> list = new List<Vector3>();
			int i;
			for (i = 0; i < filteredPositions.Count; i++)
			{
				if (list.All((Vector3 position) => Vector2.Distance(position, filteredPositions[i]) > radius))
				{
					list.Add(filteredPositions[i]);
				}
			}
			if (list.Count == 0)
			{
				return false;
			}
			count = Mathf.Min(count, list.Count);
			for (int num = 0; num < count; num++)
			{
				int index = UnityEngine.Random.Range(0, list.Count);
				positions.Add(list[index]);
				list.RemoveAt(index);
				if (list.Count == 0)
				{
					return true;
				}
			}
			return true;
		}
	}
}
