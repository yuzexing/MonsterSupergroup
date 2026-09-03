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

		protected bool IsPresentationOnly { get; private set; }

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
			throw new InvalidOperationException(
				"Legacy attack initialization is disabled. Owned attacks require " +
				"InitNative with an immutable AttackSnapshot.");
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
			IsPresentationOnly = false;
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

		public virtual void InitPresentation(
			WeaponBehaviour behaviour,
			ProjectilePresentationStats stats,
			Action onStart = null,
			Action onEnd = null)
		{
			ReleaseNativeAttackSnapshot();
			IsPresentationOnly = true;
			InitializeCommon(behaviour, onStart, onEnd);
			if ((bool)progressionScaler)
			{
				progressionScaler.Apply(stats);
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
				hitbox.Init(IsPresentationOnly ? null : OnHit);
			}
			else
			{
				DBL.Log(DBL.Module.PlayerAttacks, "No Hitbox found: make sure this attack doesn't need it!", 1);
			}
		}

		public abstract void Attack();

		protected virtual void OnHit(IDamageable damageable)
		{
			if (IsPresentationOnly)
			{
				return;
			}

			if ((bool)hitEffectResolver)
			{
				AttackSnapshot attack = RequireNativeAttackSnapshot();
				hitEffectResolver.Initialize(_behaviour, attack);
			}
			if (HitEffectMode != DamageMode.None && HitEffectMode != DamageMode.ExplosionHit)
			{
				ResolveDamage(damageable);
			}
		}

		protected void ResolveDamage(IDamageable damageable)
		{
			if (IsPresentationOnly)
			{
				return;
			}

			_behaviour.OnNativeGasHit(
				base.transform.position,
				damageable,
				RequireNativeAttackSnapshot());
		}

		private AttackSnapshot RequireNativeAttackSnapshot()
		{
			return NativeAttackSnapshot ?? throw new InvalidOperationException(
				"Owned attack has no New GAS AttackSnapshot.");
		}

		public void ReleaseNativeAttackSnapshot()
		{
			_nativeAttackLease?.Dispose();
			_nativeAttackLease = null;
		}

		public abstract void Dispose();
	}
}
