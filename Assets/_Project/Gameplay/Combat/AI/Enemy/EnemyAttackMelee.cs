using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class EnemyAttackMelee : EnemyAttack
	{
		public EnemyAttackPrefab attackPrefab;

		protected EnemyAttackPrefab _attack;

		protected EnemyAttackWarning _warning;

		protected GameObject _collidersGameObject;

		protected BaseAttackHitBox _hitBox;

		public override void AttackWarningEnter()
		{
			base.AttackWarningEnter();
			_attackPooler = PoolManager.Instance.GetOrCreatePooler(attackPrefab);
			_attack = _attackPooler.GetOrCreate(base.transform, activate: true);
			_attack.transform.position = base.transform.position;
			_attack.SetStats(base.controller.stats);
			Vector2 vector = ((!(base.Target != null)) ? base.controller.FacingDirection : ((Vector2)(base.Target.position - _attack.transform.position)));
			float z = Mathf.Atan2(vector.y, vector.x) * 57.29578f;
			_attack.transform.rotation = Quaternion.Euler(0f, 0f, z);
			_warning = _attack.attackWarning;
			_warning.SetWarningTime(base.WarningTime, base.AttackTime);
			_warning.Show();
			if ((bool)_attack.damageInteraction)
			{
				_collidersGameObject = _attack.damageInteraction.gameObject;
				_collidersGameObject.SetActive(value: false);
			}
			if ((bool)_attack.hitBox)
			{
				_hitBox = _attack.hitBox;
				_hitBox.Toggle(state: false);
			}
		}

		public override void AttackEnter()
		{
			base.AttackEnter();
			if ((bool)_attack)
			{
				_attack.EnableDamage();
			}
			_warning.Hide();
			if ((bool)_collidersGameObject)
			{
				_collidersGameObject.SetActive(value: true);
			}
			if ((bool)_hitBox)
			{
				_hitBox.Toggle(state: true);
			}
		}

		public override async void AttackExit()
		{
			await _warning.AwaitableHide();
			_attackPooler.Return(_attack);
		}

		public override void CancelAttack()
		{
			base.controller.lastAttackTime = Time.time;
			if ((bool)_collidersGameObject)
			{
				_collidersGameObject.SetActive(value: false);
			}
			if ((bool)_hitBox)
			{
				_hitBox.Toggle(state: false);
			}
			_attack.gameObject.SetActive(value: false);
			_attackPooler.Return(_attack);
		}
	}
}
