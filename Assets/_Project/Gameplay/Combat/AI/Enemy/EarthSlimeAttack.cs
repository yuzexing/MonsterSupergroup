using AstralShift.HellMaiden.Combat;
using DG.Tweening;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class EarthSlimeAttack : EnemyAttackMelee
	{
		public Ease easeType = Ease.OutQuad;

		[SerializeField]
		private float emergeDistance = 0.35f;

		[SerializeField]
		private float showWarningDistance = 1.4f;

		private bool isWarningShowing;

		public float underGroundSpeed = 5f;

		[SerializeField]
		private ParticleSystem earthTrail;

		[Header("Jump Settings")]
		[SerializeField]
		private float jumpPower = 1.5f;

		[SerializeField]
		private int numJumps = 1;

		[SerializeField]
		private float jumpDuration = 0.5f;

		[SerializeField]
		private float jumpDelay = 0.5f;

		public LayerMask underGroundCollision;

		private LayerMask defaultLayer;

		private void Start()
		{
			defaultLayer = base.controller.collider.excludeLayers;
			earthTrail.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmittingAndClear);
		}

		public override void AttackWarningEnter()
		{
			base.controller.collider.excludeLayers = underGroundCollision;
			base.controller.hurtBox.gameObject.SetActive(value: false);
			isWarningShowing = false;
			earthTrail.Clear(withChildren: true);
			earthTrail.Play(withChildren: true);
			_attackPooler = PoolManager.Instance.GetOrCreatePooler(attackPrefab);
			_attack = _attackPooler.GetOrCreate(base.transform, activate: true);
			_attack.transform.position = base.transform.position;
			_attack.SetStats(base.controller.stats);
			_warning = _attack.attackWarning;
			_warning.SetWarningTime(base.WarningTime, base.AttackTime);
			if ((bool)_attack.damageInteraction)
			{
				_collidersGameObject = _attack.damageInteraction.gameObject;
				_collidersGameObject.SetActive(value: false);
			}
		}

		public override void AttackWarningExit()
		{
			base.AttackWarningExit();
			base.controller.collider.excludeLayers = defaultLayer;
			base.controller.hurtBox.gameObject.SetActive(value: true);
			earthTrail.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmitting);
		}

		public override void AttackWarningTick()
		{
			if ((base.transform.position - base.Target.position).sqrMagnitude <= emergeDistance * emergeDistance && !isWarningShowing)
			{
				_warningStartTime = Time.time;
				isWarningShowing = true;
				_warning.Show();
			}
			if (isWarningShowing)
			{
				float num = Time.time - _warningStartTime;
				OnWarningTick?.Invoke(num / base.WarningTime);
				if (num > base.WarningTime)
				{
					Vector2 previousFacingDirection = (base.Target.position - base.transform.position).normalized;
					base.controller.previousFacingDirection = previousFacingDirection;
					onAttackWarningEnd?.Invoke();
				}
			}
			else
			{
				base.transform.position = Vector3.MoveTowards(base.transform.position, base.Target.position, underGroundSpeed * Time.deltaTime);
			}
		}

		public override void AttackEnter()
		{
			base.AttackEnter();
			Invoke("Jump", jumpDelay);
		}

		private void Jump()
		{
			base.transform.DOJump(base.transform.position, jumpPower, numJumps, jumpDuration);
		}
	}
}
