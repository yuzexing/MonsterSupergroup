using System.Collections;
using AstralShift.HellMaiden.Combat;
using AstralShift.Pooling;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Boss.Scarmiglione
{
	public class FireWavesAttackBehaviour : BossAttackBehaviour
	{
		public FireHedges[] fireHedges;

		[SerializeField]
		private int numberPerAttack = 3;

		public Transform[] positions;

		public float hedgeAttackBetweenAttacksTimer = 0.5f;

		private GenericPooler<FireHedges>[] _pooler;

		[SerializeField]
		private Transform centerPosition;

		[SerializeField]
		private bool stopWhileAttacking;

		private void Start()
		{
			if (fireHedges.Length != positions.Length)
			{
				Debug.LogError("Positions must be the same amount as prefabs!");
			}
			_pooler = new GenericPooler<FireHedges>[positions.Length];
		}

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
				StartCoroutine(AttackRoutine());
			});
		}

		private IEnumerator AttackRoutine()
		{
			if (!stopWhileAttacking)
			{
				onAttackEnd?.Invoke();
			}
			int n = numberPerAttack;
			while (n > 0)
			{
				int posIdx = Random.Range(0, positions.Length);
				GenericPooler<FireHedges>[] pooler = _pooler;
				int num = posIdx;
				if (pooler[num] == null)
				{
					pooler[num] = PoolManager.Instance.GetOrCreatePooler(fireHedges[posIdx]);
				}
				FireHedges f = _pooler[posIdx].GetOrCreate();
				f.transform.position = positions[posIdx].position;
				f.onDespawn = delegate
				{
					_pooler[posIdx].Return(f);
				};
				f.gameObject.SetActive(value: true);
				n--;
				yield return new WaitForSeconds(hedgeAttackBetweenAttacksTimer);
			}
			if (stopWhileAttacking)
			{
				onAttackEnd?.Invoke();
			}
			yield return null;
		}

		public override void Dispose()
		{
		}
	}
}
