using AstralShift.HellMaiden.Combat.Hand;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class PersistentAreaAttackBehaviour : WeaponBehaviour
	{
		public AnimatedAttack attack;

		public override void Init(uint id, AttackStats stats)
		{
			base.Init(id, stats);
			attack.gameObject.SetActive(value: true);
			attack.Init(this);
			SetSpeed();
			attack.PlayStartAnimationYield();
		}

		public virtual void Update()
		{
			if (CheckCooldown())
			{
				Attack();
				LastAttackElapsedTime = 0f;
			}
			LastAttackElapsedTime += Time.deltaTime;
		}

		public override void Attack()
		{
			base.Attack();
			attack.PlayHitAnimation();
		}

		public override void UpdateModifiers(RuntimeEquipmentModifiers runtimeModifiers)
		{
			base.UpdateModifiers(runtimeModifiers);
			attack.Init(this);
			SetSpeed();
		}

		private void SetSpeed()
		{
			(attack.hitbox as PlayerAttackOvertimeHitBox)?.SetHitInterval(1f / base.SpeedValue);
		}

		protected override void Dispose()
		{
			attack.gameObject.SetActive(value: false);
		}
	}
}
