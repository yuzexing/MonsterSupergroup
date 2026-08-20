using System.Collections;
using AstralShift.HellMaiden.AI.Boss;
using AstralShift.HellMaiden.AI.Boss.Minos;
using AstralShift.HellMaiden.Combat;
using AstralShift.Pooling;
using AstralShift.QTI.Helpers.Attributes;
using FMODUnity;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy.Boss.Libicocco.Attacks
{
	public class CloneCircleAttack : BossAttackBehaviour
	{
		private GenericPooler<LibicoccoClone> pooler;

		[SerializeField]
		private LibicoccoClone clone;

		[SerializeField]
		private Transform[] targetPositions;

		[SerializeField]
		private float speed = 30f;

		[SerializeField]
		private float waitTimeBetweenClones = 2f;

		[SerializeField]
		private float waitTimeAfterShots = 2f;

		[SerializeField]
		private bool twoAtATime;

		[SerializeField]
		private bool walkAndShoot;

		[SerializeField]
		[ConditionalHide("walkAndShoot", true)]
		private float waitTimeBetweenShots = 3.5f;

		[SerializeField]
		private bool moveBossToo;

		[SerializeField]
		[ConditionalHide("moveBossToo", true)]
		private Transform bossDestination;

		[SerializeField]
		[ConditionalHide("moveBossToo", true)]
		private float waitTimeAfterBossMovement = 2f;

		[SerializeField]
		private bool bossShootToo;

		[SerializeField]
		[ConditionalHide("walkAndShoot", false)]
		private bool infinite;

		[SerializeField]
		[Tooltip("I lied, its not infinite")]
		private float cloneAttacks;

		private LibicoccoClone[] _clones;

		[SerializeField]
		private EventReference cloneSound;

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
				StartCoroutine(SpawnClones());
			});
		}

		private IEnumerator SpawnClones()
		{
			if (pooler == null)
			{
				pooler = PoolManager.Instance.GetOrCreatePooler(this.clone);
			}
			Vector3 position1 = base.transform.position;
			Vector3 position2 = base.transform.position;
			_clones = new LibicoccoClone[targetPositions.Length];
			if (twoAtATime)
			{
				for (int i = 0; i < targetPositions.Length; i += 2)
				{
					_clones[i] = SpawnAndMoveClone(ref position1, i);
					_clones[i + 1] = SpawnAndMoveClone(ref position2, i + 1);
					yield return new WaitForSeconds(waitTimeBetweenClones);
				}
			}
			else
			{
				for (int i = 0; i < targetPositions.Length; i++)
				{
					_clones[i] = SpawnAndMoveClone(ref position1, i);
					yield return new WaitForSeconds(waitTimeBetweenClones);
				}
			}
			if (moveBossToo)
			{
				(bossController.movementController as MinosMovementController).SetDestination(bossDestination.position);
				yield return new WaitForSeconds(waitTimeAfterBossMovement);
			}
			if (walkAndShoot)
			{
				yield return StartCoroutine(MoveClonesAndShoot(_clones));
			}
			else
			{
				do
				{
					yield return StartCoroutine(AttackWithClones(_clones));
					cloneAttacks -= 1f;
				}
				while (cloneAttacks > 0f);
			}
			for (int j = 0; j < _clones.Length; j++)
			{
				LibicoccoClone clone = _clones[j];
				if (clone != null)
				{
					clone.Despawn(delegate
					{
						pooler.Return(clone);
					});
				}
			}
			onAttackEnd?.Invoke();
			yield return null;
		}

		public IEnumerator AttackWithClones(LibicoccoClone[] clones)
		{
			if (bossShootToo)
			{
				shooter.ShootBullets();
			}
			for (int i = 0; i < clones.Length; i++)
			{
				clones[i].Shoot();
			}
			yield return new WaitForSeconds(waitTimeAfterShots);
		}

		private IEnumerator MoveClonesAndShoot(LibicoccoClone[] clones)
		{
			for (int i = 0; i < clones.Length; i++)
			{
				clones[i].Shoot();
			}
			yield return new WaitForSeconds(waitTimeBetweenShots);
			for (int atk = 0; atk < targetPositions.Length; atk++)
			{
				for (int j = 0; j < targetPositions.Length; j++)
				{
					int num = j + atk + 1;
					num %= targetPositions.Length;
					LibicoccoClone currentClone = clones[j];
					currentClone.SetDestination(targetPositions[num].position, delegate
					{
						currentClone.Shoot();
					}, speed);
				}
				yield return new WaitForSeconds(waitTimeBetweenShots);
			}
			yield return new WaitForSeconds(waitTimeAfterShots);
		}

		private LibicoccoClone SpawnAndMoveClone(ref Vector3 position, int idx)
		{
			RuntimeManager.PlayOneShot(cloneSound);
			LibicoccoClone orCreate = pooler.GetOrCreate(null, activate: true);
			orCreate.transform.position = position;
			orCreate.AssignController(bossController);
			position = targetPositions[idx].position;
			orCreate.SetDestination(position, null, speed);
			return orCreate;
		}

		public override void Stop()
		{
			StopAllCoroutines();
			if (_clones == null)
			{
				return;
			}
			for (int i = 0; i < _clones.Length; i++)
			{
				LibicoccoClone clone = _clones[i];
				if (clone != null)
				{
					clone.Despawn(delegate
					{
						pooler.Return(clone);
					});
				}
			}
			_clones = null;
		}

		public override void Dispose()
		{
			StopAllCoroutines();
			if (_clones == null)
			{
				return;
			}
			for (int i = 0; i < _clones.Length; i++)
			{
				if (_clones[i] != null)
				{
					pooler.Return(_clones[i]);
				}
			}
			_clones = null;
		}
	}
}
