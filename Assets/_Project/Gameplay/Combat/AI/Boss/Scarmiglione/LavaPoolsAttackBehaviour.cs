using System.Collections;
using AstralShift.HellMaiden.Combat;
using AstralShift.Helpers;
using AstralShift.Pooling;
using AstralShift.QTI.Interactions;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Boss.Scarmiglione
{
	public class LavaPoolsAttackBehaviour : BossAttackBehaviour
	{
		[Header("References")]
		public BoxCollider2D outerArea;

		[Tooltip("Prefabs must contain a BoxCollider2D")]
		public AnimatedBossAttack[] prefabs;

		[Header("Spawn Settings")]
		public int spawnCount = 20;

		public int maxAttemptsPerSpawn = 100;

		[Tooltip("Layer used by spawned prefabs")]
		public LayerMask spawnedLayer;

		public Transform spawnParent;

		public float timeBetweenSpawns = 1f;

		private GenericPooler<AnimatedBossAttack>[] _poolers = new GenericPooler<AnimatedBossAttack>[2];

		[SerializeField]
		private float lavaPoolsTtl = 6f;

		[SerializeField]
		private float attackDuration = 3f;

		public override void Positioning()
		{
			onPositioningEnd?.Invoke();
		}

		public override void Warning()
		{
			BarkWarning();
			WarningBossAnimation(onWarningEnd);
		}

		public override void Attack()
		{
			AttackBossAnimation(delegate
			{
				StartCoroutine(SpawnAll());
			});
			StartCoroutine(Wait.SetTimeout(attackDuration, onAttackEnd));
		}

		public IEnumerator SpawnAll()
		{
			if (prefabs == null || prefabs.Length == 0)
			{
				Debug.LogError("No prefabs assigned.");
				yield return null;
			}
			for (int i = 0; i < spawnCount; i++)
			{
				TrySpawnOne();
				yield return new WaitForSeconds(timeBetweenSpawns);
			}
		}

		private void TrySpawnOne()
		{
			for (int i = 0; i < maxAttemptsPerSpawn; i++)
			{
				int chosenIdx = Random.Range(0, prefabs.Length);
				AnimatedBossAttack animatedBossAttack = prefabs[chosenIdx];
				BoxCollider2D componentInChildren = animatedBossAttack.GetComponentInChildren<BoxCollider2D>();
				if (!componentInChildren)
				{
					Debug.LogError("Prefab " + animatedBossAttack.name + " has no BoxCollider2D.");
					continue;
				}
				Vector2 randomPositionThatFits = GetRandomPositionThatFits(componentInChildren);
				if (!IsFullyInsideOuter(randomPositionThatFits, componentInChildren) || IsOverlapping(randomPositionThatFits, componentInChildren))
				{
					continue;
				}
				GenericPooler<AnimatedBossAttack>[] poolers = _poolers;
				int num = chosenIdx;
				if (poolers[num] == null)
				{
					poolers[num] = PoolManager.Instance.GetOrCreatePooler(animatedBossAttack);
				}
				AnimatedBossAttack lavaPool = _poolers[chosenIdx].GetOrCreate();
				lavaPool.transform.position = new Vector3(randomPositionThatFits.x, randomPositionThatFits.y, animatedBossAttack.transform.localPosition.z);
				SetRotationInteraction component = lavaPool.GetComponent<SetRotationInteraction>();
				component.angle = Random.Range(0f, 360f);
				component.Interact();
				lavaPool.transform.SetParent(spawnParent);
				lavaPool.gameObject.SetActive(value: true);
				lavaPool.RunInAnimation(lavaPool.RunLoopAnimation);
				StartCoroutine(Wait.SetTimeout(lavaPoolsTtl, delegate
				{
					lavaPool.RunOutAnimation(delegate
					{
						_poolers[chosenIdx].Return(lavaPool);
					});
				}));
				return;
			}
			Debug.LogWarning("Failed to spawn any prefab without overlap.");
		}

		private Vector2 GetRandomPositionThatFits(BoxCollider2D col)
		{
			Bounds bounds = outerArea.bounds;
			Vector2 vector = Vector2.Scale(col.size, col.transform.lossyScale);
			Vector2 vector2 = Vector2.Scale(col.offset, col.transform.lossyScale);
			float minInclusive = bounds.min.x - vector2.x + vector.x * 0.5f;
			float maxInclusive = bounds.max.x - vector2.x - vector.x * 0.5f;
			float minInclusive2 = bounds.min.y - vector2.y + vector.y * 0.5f;
			return new Vector2(y: Random.Range(minInclusive2, bounds.max.y - vector2.y - vector.y * 0.5f), x: Random.Range(minInclusive, maxInclusive));
		}

		private bool IsFullyInsideOuter(Vector2 position, BoxCollider2D col)
		{
			Bounds bounds = outerArea.bounds;
			Vector2 vector = Vector2.Scale(col.size, col.transform.lossyScale);
			Vector2 vector2 = Vector2.Scale(col.offset, col.transform.lossyScale);
			Vector2 vector3 = position + vector2;
			Bounds bounds2 = new Bounds(vector3, vector);
			if (bounds2.min.x >= bounds.min.x && bounds2.max.x <= bounds.max.x && bounds2.min.y >= bounds.min.y)
			{
				return bounds2.max.y <= bounds.max.y;
			}
			return false;
		}

		private bool IsOverlapping(Vector2 position, BoxCollider2D col)
		{
			Vector2 size = Vector2.Scale(col.size, col.transform.lossyScale);
			Vector2 vector = Vector2.Scale(col.offset, col.transform.lossyScale);
			return Physics2D.OverlapBox(position + vector, size, 0f, spawnedLayer) != null;
		}
	}
}
