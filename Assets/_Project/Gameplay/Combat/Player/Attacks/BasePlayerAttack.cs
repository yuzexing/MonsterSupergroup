using System;
using AstralShift.DebugTools;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public abstract class BasePlayerAttack : MonoBehaviour
	{
		[Header("General Settings")]
		public AttackProgressionScaler progressionScaler;

		public BaseAttackHitBox hitbox;

		public BaseAttackHitEffectResolver hitEffectResolver;

		protected WeaponBehaviour _behaviour;

		protected Action _onStart;

		protected Action _onEnd;

		protected DamageMode HitEffectMode
		{
			get
			{
				if ((bool)hitEffectResolver)
				{
					return hitEffectResolver.DamageMode;
				}
				return DamageMode.MainHit;
			}
		}

		public virtual void Init(WeaponBehaviour behaviour, Action onStart = null, Action onEnd = null)
		{
			_behaviour = behaviour;
			_onStart = onStart;
			_onEnd = onEnd;
			if ((bool)hitbox)
			{
				hitbox.Init(OnHit);
			}
			else
			{
				DBL.Log(DBL.Module.PlayerAttacks, "No Hitbox found: make sure this attack doesn't need it!", 1);
			}
			if ((bool)progressionScaler)
			{
				progressionScaler.Apply(behaviour);
			}
		}

		public abstract void Attack();

		protected virtual void OnHit(IDamageable damageable)
		{
			if ((bool)hitEffectResolver)
			{
				hitEffectResolver.Initialize(_behaviour);
			}
			if (HitEffectMode != DamageMode.None && HitEffectMode != DamageMode.ExplosionHit)
			{
				_behaviour.OnHit(base.transform.position, damageable);
			}
		}

		public abstract void Dispose();
	}
}
