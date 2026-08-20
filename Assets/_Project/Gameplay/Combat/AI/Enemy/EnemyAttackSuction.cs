using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Interactions;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class EnemyAttackSuction : EnemyAttack
	{
		private EnemyAttackPrefab area;

		private ParticleSystem attackParticles;

		private SpriteRenderer attackWarning;

		private ParticleSystem attackWarningParticles;

		private GameObject attackColliders;

		private Animator attackWarningAnimator;

		public EnemyAttackPrefab attackPrefab;

		public PlayerDamageInteraction damageInteraction;

		public override void AttackWarningEnter()
		{
			damageInteraction.enemyStats = base.controller.stats;
			base.AttackWarningEnter();
			_attackPooler = PoolManager.Instance.GetOrCreatePooler(attackPrefab);
			area = _attackPooler.GetOrCreate(base.transform, activate: true);
			area.transform.position = base.transform.position;
			area.SetStats(base.controller.stats);
			Vector3 vector = GameDirector.Instance.Player.transform.position - area.transform.position;
			float z = Mathf.Atan2(vector.y, vector.x) * 57.29578f;
			area.transform.rotation = Quaternion.Euler(0f, 0f, z);
			attackWarningAnimator = area.GetComponentInChildren<Animator>(includeInactive: true);
			attackWarning = attackWarningAnimator.GetComponentInChildren<SpriteRenderer>(includeInactive: true);
			attackWarning.enabled = true;
			ParticleSystem[] componentsInChildren = area.GetComponentsInChildren<ParticleSystem>(includeInactive: true);
			foreach (ParticleSystem particleSystem in componentsInChildren)
			{
				Transform parent = particleSystem.transform.parent;
				if (parent == area.transform)
				{
					attackParticles = particleSystem;
				}
				if (parent == attackWarning.transform)
				{
					attackWarningParticles = particleSystem;
				}
			}
			attackWarning.transform.localScale = Vector3.zero;
			attackColliders = area.GetComponentInChildren<Collider2D>(includeInactive: true).gameObject;
			attackColliders.SetActive(value: false);
		}

		public override void AttackWarningTick()
		{
			float num = Time.time - _warningStartTime;
			Vector3 localScale = Vector3.one * Mathf.Lerp(0f, 1f, num / base.WarningTime);
			attackWarning.transform.localScale = localScale;
			float value = Mathf.Lerp(0.5f, 0f, num / base.WarningTime);
			attackWarning.material.SetFloat("_PinchUvAmount", value);
			base.AttackWarningTick();
		}

		public override void AttackEnter()
		{
			base.AttackEnter();
			attackWarning.transform.localScale = Vector3.one;
			attackWarning.material.SetFloat("_PinchUvAmount", 0f);
			attackWarning.enabled = false;
			attackColliders.SetActive(value: true);
		}

		public override void AttackExit()
		{
			area.gameObject.SetActive(value: false);
			_attackPooler.Return(area);
		}

		public override void CancelAttack()
		{
			attackWarning.enabled = false;
			attackWarningParticles.Clear();
			attackColliders.SetActive(value: false);
			area.gameObject.SetActive(value: false);
			_attackPooler.Return(area);
		}
	}
}
