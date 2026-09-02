using System;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Data.Cards;
using FMODUnity;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;
using CombatTags = MonsterSupergroup.GAS.CombatTags;
using GasAttackSnapshot = MonsterSupergroup.GAS.AttackSnapshot;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public abstract class WeaponBehaviour : MonoBehaviour
	{
		[SerializeField]
		protected WeaponBehaviourStats statsBehaviour;

		protected PlayerMovement player;

		protected uint _id;

		protected RuntimeEquipmentModifiers _equipmentModifiers;

		private WeaponRuntimeBehaviour _nativeRuntime;

		private WeaponData _weaponData;

		[Header("Sounds")]
		[SerializeField]
		protected EventReference attackSound;

		public uint ID => _id;

		public RuntimeEquipmentModifiers EquipmentModifiers => _equipmentModifiers;

		public PlayerMovement OwnerPlayer => player;

		public PlayerCombatantBinding OwnerCombatant => player != null
			? player.CombatantBinding
			: null;

		public AttackStats BaseAttackStats => StatsBehaviour.BaseStats;

		public AttackStatsMultipliers BaseStatsMultipliers => StatsBehaviour.BaseStatsMultipliers;

		public int DamageValue => RequireNativeRuntime().Stats.DamageValue;

		public float DamageMultiplierSum =>
			RequireNativeRuntime().Stats.DamageMultiplierSum;

		public float SizeValue => RequireNativeRuntime().Stats.SizeValue;

		public float SizeMultiplierSum =>
			RequireNativeRuntime().Stats.SizeMultiplierSum;

		public float SpeedValue => RequireNativeRuntime().Stats.SpeedValue;

		public float SpeedMultiplierSum =>
			RequireNativeRuntime().Stats.SpeedMultiplierSum;

		public float SpeedMultipliersProduct =>
			RequireNativeRuntime().Stats.SpeedMultipliersProduct;

		public float DurationValue => RequireNativeRuntime().Stats.DurationValue;

		public float DurationMultiplierSum =>
			RequireNativeRuntime().Stats.DurationMultiplierSum;

		public int ProjectileCountValue =>
			RequireNativeRuntime().Stats.ProjectileCountValue;

		public float CritRate => RequireNativeRuntime().Stats.CritRate;

		public float CritRateMultiplierSum =>
			RequireNativeRuntime().Stats.CritRateMultiplierSum;

		public float CritMultiplier =>
			RequireNativeRuntime().Stats.CritDamageMultiplier;

		public float CritMultiplierSum =>
			RequireNativeRuntime().Stats.CritDamageMultiplierSum;

		public float KnockBackDistance =>
			RequireNativeRuntime().Stats.KnockBackDistance;

		public float KnockBackMultiplierSum =>
			RequireNativeRuntime().Stats.KnockBackMultiplierSum;

		public KnockbackSettings KnockbackSettings =>
			RequireWeaponData().Presentation.Knockback;

		public WeaponRuntimeBehaviour NativeRuntime => _nativeRuntime;

		public WeaponData WeaponData => _weaponData;

		public virtual float LastAttackElapsedTime { get; protected set; }

		public WeaponBehaviourStats StatsBehaviour => statsBehaviour;

		protected bool IsPoisonType { get; private set; }

		protected bool IsFireType { get; private set; }

		protected AttackElement ActiveElement
		{
			get
			{
				if (IsFireType)
				{
					return AttackElement.Fire;
				}
				if (IsPoisonType)
				{
					return AttackElement.Poison;
				}
				return AttackElement.Default;
			}
		}

		public event Action OnWeaponHit;

		public event Action<float, bool> OnWeaponDamage;

		protected virtual CombatTags DefaultCombatTags => CombatTags.Attack;

		public virtual void Init(uint id, AttackStats stats)
		{
			throw LegacyRuntimeDisabled();
		}

		public virtual void InitNative(uint id)
		{
			_id = id;
			if (player == null)
			{
				throw new InvalidOperationException(
					"Native WeaponBehaviour requires an explicitly configured owning PlayerMovement.");
			}
			RequireNativeRuntime();
			RequireWeaponData();
		}

		public void ConfigureNativeRuntime(
			WeaponRuntimeBehaviour runtime,
			WeaponData weaponData)
		{
			_nativeRuntime = runtime != null
				? runtime
				: throw new ArgumentNullException(nameof(runtime));
			_weaponData = weaponData != null
				? weaponData
				: throw new ArgumentNullException(nameof(weaponData));
			_weaponData.ValidateNativeGas();
		}

		public void ConfigureOwner(PlayerMovement owner)
		{
			player = owner != null
				? owner
				: throw new ArgumentNullException(nameof(owner));
		}

		protected abstract void Dispose();

		public virtual void Attack()
		{
			RequireNativeRuntime();
		}

		protected GasAttackSnapshot BeginNativeGasAttack()
		{
			return RequireNativeRuntime().BeginAttack(
				RequireWeaponData().AttackTags);
		}

		public LegacyDamageSource GetDamageSource(CombatTags tags = CombatTags.None)
		{
			throw LegacyRuntimeDisabled();
		}

		protected void BeginCombatAttack(CombatTags tags = CombatTags.None)
		{
			throw LegacyRuntimeDisabled();
		}

		public virtual void Activate()
		{
			base.gameObject.SetActive(value: true);
		}

		public virtual void Deactivate()
		{
			base.gameObject.SetActive(value: false);
			Dispose();
		}

		public void RedirectStat(AttackStatType target, AttackStatType source)
		{
			throw LegacyRuntimeDisabled();
		}

		public void ResetStatRedirects()
		{
			throw LegacyRuntimeDisabled();
		}

		protected virtual bool CheckCooldown()
		{
			if (LastAttackElapsedTime >= GetCooldown())
			{
				return true;
			}
			return false;
		}

		public virtual float GetCooldown()
		{
			return 1f / SpeedValue;
		}

		public virtual float GetAttacksPerSecond(AttackStats stats)
		{
			return GetAttacksPerSecond(stats.speed);
		}

		public virtual float GetAttacksPerSecond(float speedValue)
		{
			return 1f / (1f / speedValue);
		}

		public virtual float GetAttacksPerSecond()
		{
			return 1f / GetCooldown();
		}

		public virtual void Damage(Vector2 position, IDamageable damageable)
		{
			throw LegacyRuntimeDisabled();
		}

		public virtual void OnHit(Vector2 position, IDamageable damageable)
		{
			throw LegacyRuntimeDisabled();
		}

		public virtual void OnNativeGasHit(
			Vector2 position,
			IDamageable damageable,
			GasAttackSnapshot attack)
		{
			if (attack == null)
			{
				throw new ArgumentNullException(nameof(attack));
			}

			WeaponRuntimeBehaviour runtime = RequireNativeRuntime();
			WeaponData weaponData = RequireWeaponData();
			this.OnWeaponHit?.Invoke();
			if (damageable is INativeGasDamageable nativeDamageable)
			{
				nativeDamageable.ResolveNativeGasHit(new NativeGasHit(
					position,
					this,
					runtime,
					attack,
					ToLegacyDamageType(attack.Stats.DamageType),
					weaponData.Presentation.Knockback));
			}
		}

		public void NotifyNativeDamage(float value, bool isCritical)
		{
			RequireNativeRuntime();
			this.OnWeaponDamage?.Invoke(value, isCritical);
		}

		public virtual void UpdateModifiers(RuntimeEquipmentModifiers runtimeModifiers)
		{
			throw LegacyRuntimeDisabled();
		}

		protected void EvaluateDynamicStatModifiers()
		{
			throw LegacyRuntimeDisabled();
		}

		protected virtual void EvaluateDynamicOnDamageStatModifiers(BaseEnemyController enemy)
		{
			throw LegacyRuntimeDisabled();
		}

		public DamageInfo CalculateDamage(BaseEnemyController enemy)
		{
			throw LegacyRuntimeDisabled();
		}

		private WeaponRuntimeBehaviour RequireNativeRuntime()
		{
			if (_nativeRuntime == null || !_nativeRuntime.IsInitialized)
			{
				throw new InvalidOperationException(
					"WeaponBehaviour must be initialized by its owning " +
					"PlayerBuildRuntime before gameplay execution.");
			}

			return _nativeRuntime;
		}

		private WeaponData RequireWeaponData()
		{
			return _weaponData ?? throw new InvalidOperationException(
				"WeaponBehaviour has no canonical WeaponData.");
		}

		private static InvalidOperationException LegacyRuntimeDisabled()
		{
			return new InvalidOperationException(
				"Legacy Weapon damage/modifier execution is disabled. " +
				"Use WeaponData through the owning PlayerBuildRuntime and New GAS pipeline.");
		}

		private static DamageType ToLegacyDamageType(
			MonsterSupergroup.GAS.DamageType damageType)
		{
			switch (damageType)
			{
			case MonsterSupergroup.GAS.DamageType.Fire:
				return DamageType.Fire;
			case MonsterSupergroup.GAS.DamageType.Poison:
				return DamageType.Poison;
			case MonsterSupergroup.GAS.DamageType.Bleed:
				return DamageType.Bleed;
			default:
				return DamageType.Normal;
			}
		}

		public void PlayAttackSound()
		{
			if (!attackSound.IsNull)
			{
				RuntimeManager.PlayOneShot(attackSound, base.transform.position);
			}
		}
	}
}
