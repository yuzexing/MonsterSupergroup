using AstralShift.HellMaiden.Combat;
using FMODUnity;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class ProjectileConsecutiveAttacks : EnemyProjectileAttack
	{
		public int consecutiveAttacks = 1;

		private int shotsFired;

		private float shotInterval;

		private float elapsedTime;

		[SerializeField]
		private bool playSoundEachConsecutiveAttack;

		[SerializeField]
		private EventReference attackSound;

		public override void AttackWarningEnter()
		{
			bulletPooler = PoolManager.Instance.GetOrCreatePooler(bulletPrefab);
			base.controller.Movement.FreezeRigidbody(state: true);
			_warningStartTime = Time.time;
		}

		public override void AttackEnter()
		{
			base.AttackEnter();
			elapsedTime = 0f;
			shotsFired = 0;
			shotInterval = base.AttackTime / (float)consecutiveAttacks;
		}

		public override void AttackTick()
		{
			base.AttackTick();
			elapsedTime += Time.deltaTime;
			if (shotsFired < consecutiveAttacks && (shotsFired == 0 || elapsedTime >= shotInterval))
			{
				float num = Mathf.Abs(bulletPosition.transform.localPosition.x);
				bulletPosition.transform.localPosition = new Vector3((enemyController.FacingDirection.x < 0f) ? (0f - num) : num, bulletPosition.transform.localPosition.y, bulletPosition.transform.localPosition.z);
				BulletProjectile bulletProjectile = bulletPooler.GetOrCreate(bulletPosition);
				bulletProjectile.OnReturn = delegate
				{
					ReturnAttack(bulletProjectile);
				};
				bulletProjectile.transform.localPosition = Vector3.zero;
				bulletProjectile.fired = false;
				bulletProjectile.SetStats(base.controller.stats);
				bulletProjectile.gameObject.SetActive(value: true);
				direction = base.Target.position - bulletPosition.position;
				if (rotateAttack)
				{
					float angle = Mathf.Atan2(direction.y, direction.x) * 57.29578f;
					bulletProjectile.transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
				}
				bulletProjectile.transform.parent = null;
				bulletProjectile.Fire(direction);
				shotsFired++;
				elapsedTime = 0f;
				if (playSoundEachConsecutiveAttack)
				{
					RuntimeManager.PlayOneShot(attackSound);
				}
			}
		}
	}
}
