using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class DashAttackBehaviour : WeaponBehaviour
	{
		[Header("Attack Settings")]
		[SerializeField]
		private BasePlayerAttackVariants variants;

		public override void Init(uint id, AttackStats stats)
		{
			base.Init(id, stats);
			variants.Init();
			if ((bool)player)
			{
				player.OnDashStart += Attack;
			}
			LastAttackElapsedTime = GetCooldown() - Time.deltaTime;
		}

		private BasePlayerAttack GetOrCreateAttack()
		{
			BasePlayerAttack attack = variants.GetOrCreate(base.ActiveElement, null, worldPositionStays: true);
			attack.Init(this, null, OnEnd);
			return attack;
			void OnEnd()
			{
				variants.Return(attack);
			}
		}

		public override float GetCooldown()
		{
			return 1f;
		}

		private void OnDestroy()
		{
			Dispose();
		}

		protected override void Dispose()
		{
			variants.Dispose(delegate(BasePlayerAttack attack)
			{
				attack.Dispose();
			});
			if ((bool)player)
			{
				player.OnDashStart -= Attack;
			}
		}

		public override void Attack()
		{
			base.Attack();
			GetOrCreateAttack().Attack();
		}
	}
}
