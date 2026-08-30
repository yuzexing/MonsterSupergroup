using System;
using AstralShift.DebugTools;
using MonsterSupergroup.GAS;
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

		private AttackSnapshotLease _nativeAttackLease;

		protected AttackSnapshot NativeAttackSnapshot =>
			_nativeAttackLease?.Snapshot;

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
			ReleaseNativeAttackSnapshot();
			InitializeCommon(behaviour, onStart, onEnd);
			if ((bool)progressionScaler)
			{
				progressionScaler.Apply(behaviour);
			}
		}

		public virtual void InitNative(
			WeaponBehaviour behaviour,
			AttackSnapshot attack,
			Action onStart = null,
			Action onEnd = null)
		{
			if (attack == null)
			{
				throw new ArgumentNullException(nameof(attack));
			}

			ReleaseNativeAttackSnapshot();
			_nativeAttackLease = attack.Retain();
			try
			{
				InitializeCommon(behaviour, onStart, onEnd);
				if ((bool)progressionScaler)
				{
					progressionScaler.Apply(attack.Stats);
				}
			}
			catch
			{
				ReleaseNativeAttackSnapshot();
				throw;
			}
		}

		private void InitializeCommon(
			WeaponBehaviour behaviour,
			Action onStart,
			Action onEnd)
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
		}

		public abstract void Attack();

		protected virtual void OnHit(IDamageable damageable)
		{
			if ((bool)hitEffectResolver)
			{
				if (NativeAttackSnapshot != null)
				{
					hitEffectResolver.Initialize(_behaviour, NativeAttackSnapshot);
				}
				else
				{
					hitEffectResolver.Initialize(_behaviour);
				}
			}
			if (HitEffectMode != DamageMode.None && HitEffectMode != DamageMode.ExplosionHit)
			{
				ResolveDamage(damageable);
			}
		}

		protected void ResolveDamage(IDamageable damageable)
		{
			if (NativeAttackSnapshot != null)
			{
				_behaviour.OnNativeGasHit(
					base.transform.position,
					damageable,
					NativeAttackSnapshot);
			}
			else
			{
				_behaviour.OnHit(base.transform.position, damageable);
			}
		}

		public void ReleaseNativeAttackSnapshot()
		{
			_nativeAttackLease?.Dispose();
			_nativeAttackLease = null;
		}

		public abstract void Dispose();
	}
}
