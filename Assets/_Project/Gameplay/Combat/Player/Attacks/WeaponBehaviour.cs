using System;
using System.Collections.Generic;
using System.Linq;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Combat.Hand;
using FMODUnity;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;
using CombatContext = MonsterSupergroup.GAS.CombatContext;
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

		private CombatRuntimeServiceProvider _combatServiceProvider;

		private LegacyCombatExecution _combatExecution;

		private CombatContext _currentAttackContext;

		private WeaponRuntimeBehaviour _nativeRuntime;

		private NativeGasWeaponDefinition _nativeDefinition;

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

		public int DamageValue => UsesNativeGasRuntime
			? _nativeRuntime.Stats.DamageValue
			: StatsBehaviour.DamageValue;

		public float DamageMultiplierSum => UsesNativeGasRuntime
			? _nativeRuntime.Stats.DamageMultiplierSum
			: StatsBehaviour.DamageMultiplierSum;

		public float SizeValue => UsesNativeGasRuntime
			? _nativeRuntime.Stats.SizeValue
			: StatsBehaviour.SizeValue;

		public float SizeMultiplierSum => UsesNativeGasRuntime
			? _nativeRuntime.Stats.SizeMultiplierSum
			: StatsBehaviour.SizeMultiplierSum;

		public float SpeedValue => UsesNativeGasRuntime
			? _nativeRuntime.Stats.SpeedValue
			: StatsBehaviour.SpeedValue;

		public float SpeedMultiplierSum => UsesNativeGasRuntime
			? _nativeRuntime.Stats.SpeedMultiplierSum
			: StatsBehaviour.SpeedMultiplierSum;

		public float SpeedMultipliersProduct => UsesNativeGasRuntime
			? _nativeRuntime.Stats.SpeedMultipliersProduct
			: StatsBehaviour.SpeedMultipliersProduct;

		public float DurationValue => UsesNativeGasRuntime
			? _nativeRuntime.Stats.DurationValue
			: StatsBehaviour.DurationValue;

		public float DurationMultiplierSum => UsesNativeGasRuntime
			? _nativeRuntime.Stats.DurationMultiplierSum
			: StatsBehaviour.DurationMultiplierSum;

		public int ProjectileCountValue => UsesNativeGasRuntime
			? _nativeRuntime.Stats.ProjectileCountValue
			: StatsBehaviour.ProjectileCountValue;

		public float CritRate => UsesNativeGasRuntime
			? _nativeRuntime.Stats.CritRate
			: StatsBehaviour.CritRate;

		public float CritRateMultiplierSum => UsesNativeGasRuntime
			? _nativeRuntime.Stats.CritRateMultiplierSum
			: StatsBehaviour.CritRateMultiplierSum;

		public float CritMultiplier => UsesNativeGasRuntime
			? _nativeRuntime.Stats.CritDamageMultiplier
			: StatsBehaviour.CritDamageMultiplier;

		public float CritMultiplierSum => UsesNativeGasRuntime
			? _nativeRuntime.Stats.CritDamageMultiplierSum
			: StatsBehaviour.CritDamageMultiplierSum;

		public float KnockBackDistance => UsesNativeGasRuntime
			? _nativeRuntime.Stats.KnockBackDistance
			: StatsBehaviour.KnockBackDistance;

		public float KnockBackMultiplierSum => UsesNativeGasRuntime
			? _nativeRuntime.Stats.KnockBackMultiplierSum
			: StatsBehaviour.KnockBackMultiplierSum;

		public KnockbackSettings KnockbackSettings => UsesNativeGasRuntime
			? _nativeDefinition.KnockbackPresentation
			: StatsBehaviour.BaseStats.knockbackSettings;

		public bool UsesNativeGasRuntime => _nativeRuntime != null &&
			_nativeDefinition != null && _nativeRuntime.IsInitialized;

		public WeaponRuntimeBehaviour NativeRuntime => _nativeRuntime;

		public NativeGasWeaponDefinition NativeDefinition => _nativeDefinition;

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

		public CombatContext CurrentAttackContext => _currentAttackContext;

		protected virtual CombatTags DefaultCombatTags => CombatTags.Attack;

		public virtual void Init(uint id, AttackStats stats)
		{
			_id = id;
			if (player == null)
			{
				player = GameDirector.Instance.Player;
			}
			if (player == null)
			{
				throw new InvalidOperationException(
					"WeaponBehaviour requires an owning PlayerMovement before initialization.");
			}
			statsBehaviour = new WeaponBehaviourStats(stats, player.PlayerStats);
		}

		public void ConfigureNativeRuntime(
			WeaponRuntimeBehaviour runtime,
			NativeGasWeaponDefinition definition)
		{
			_nativeRuntime = runtime != null
				? runtime
				: throw new ArgumentNullException(nameof(runtime));
			_nativeDefinition = definition != null
				? definition
				: throw new ArgumentNullException(nameof(definition));
			_nativeDefinition.Validate();
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
			if (UsesNativeGasRuntime)
			{
				return;
			}

			EvaluateDynamicStatModifiers();
		}

		protected GasAttackSnapshot BeginNativeGasAttack()
		{
			if (!UsesNativeGasRuntime)
			{
				throw new InvalidOperationException(
					"Weapon has not been configured for the New GAS native runtime.");
			}

			return _nativeRuntime.BeginAttack(_nativeDefinition.AttackTags);
		}

		public LegacyDamageSource GetDamageSource(CombatTags tags = CombatTags.None)
		{
			RejectLegacyExecutionForNativeWeapon();
			EnsureCombatExecution();
			if (!_currentAttackContext.IsValid)
			{
				_currentAttackContext = _combatExecution.BeginAttack(
					_id,
					DefaultCombatTags);
			}

			return new LegacyDamageSource(
				_combatExecution,
				_currentAttackContext,
				_id,
				tags);
		}

		protected void BeginCombatAttack(CombatTags tags = CombatTags.None)
		{
			RejectLegacyExecutionForNativeWeapon();
			EnsureCombatExecution();
			_currentAttackContext = _combatExecution.BeginAttack(
				_id,
				tags == CombatTags.None ? DefaultCombatTags : tags);
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
			StatsBehaviour.RemapStat(target, source);
		}

		public void ResetStatRedirects()
		{
			StatsBehaviour.ResetStatRemaps();
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
			RejectLegacyExecutionForNativeWeapon();
			damageable?.Damage(position, this, StatsBehaviour.BaseStats.damageType);
		}

		public virtual void OnHit(Vector2 position, IDamageable damageable)
		{
			RejectLegacyExecutionForNativeWeapon();
			this.OnWeaponHit?.Invoke();
			Damage(position, damageable);
		}

		public virtual void OnNativeGasHit(
			Vector2 position,
			IDamageable damageable,
			GasAttackSnapshot attack)
		{
			if (!UsesNativeGasRuntime)
			{
				throw new InvalidOperationException(
					"Native hit resolution requires a configured native weapon runtime.");
			}

			if (attack == null)
			{
				throw new ArgumentNullException(nameof(attack));
			}

			this.OnWeaponHit?.Invoke();
			if (damageable is INativeGasDamageable nativeDamageable)
			{
				nativeDamageable.ResolveNativeGasHit(new NativeGasHit(
					position,
					this,
					_nativeRuntime,
					attack,
					ToLegacyDamageType(attack.Stats.DamageType),
					_nativeDefinition.KnockbackPresentation));
			}
		}

		public void NotifyNativeDamage(float value, bool isCritical)
		{
			if (!UsesNativeGasRuntime)
			{
				throw new InvalidOperationException(
					"Native damage notification requires a configured native weapon runtime.");
			}

			this.OnWeaponDamage?.Invoke(value, isCritical);
		}

		public virtual void UpdateModifiers(RuntimeEquipmentModifiers runtimeModifiers)
		{
			if (UsesNativeGasRuntime)
			{
				throw new InvalidOperationException(
					"Legacy equipment modifiers cannot be attached to a New GAS native weapon.");
			}

			_equipmentModifiers = runtimeModifiers;
			IsPoisonType = _equipmentModifiers.OnHitModifiers.Any((OnHitModifier m) => m is OnHitPoisonModifier);
			IsFireType = _equipmentModifiers.OnHitModifiers.Any((OnHitModifier m) => m is OnHitBurnModifier);
			EvaluateStaticStatModifiers();
		}

		private void EvaluateStaticStatModifiers()
		{
			StatsBehaviour.BaseStatsMultipliers.Reset();
			List<StaticStatModifier> staticModifiers = _equipmentModifiers.StaticModifiers;
			if (staticModifiers == null)
			{
				return;
			}
			for (int i = 0; i < staticModifiers.Count; i++)
			{
				if (staticModifiers[i] != null)
				{
					staticModifiers[i].Apply(StatsBehaviour);
				}
			}
		}

		protected void EvaluateDynamicStatModifiers()
		{
			StatsBehaviour.DynamicStatsMultipliers.Reset();
			List<DynamicStatModifier> dynamicModifiers = _equipmentModifiers.DynamicModifiers;
			if (dynamicModifiers != null)
			{
				for (int i = 0; i < dynamicModifiers.Count; i++)
				{
					if (dynamicModifiers[i] != null)
					{
						dynamicModifiers[i].Apply(StatsBehaviour, this);
					}
				}
			}

			BeginCombatAttack();
		}

		protected virtual void EvaluateDynamicOnDamageStatModifiers(BaseEnemyController enemy)
		{
			List<DynamicOnDamageModifier> dynamicOnDamageModifiers = _equipmentModifiers.DynamicOnDamageModifiers;
			if (dynamicOnDamageModifiers == null)
			{
				return;
			}
			for (int i = 0; i < dynamicOnDamageModifiers.Count; i++)
			{
				if (dynamicOnDamageModifiers[i] != null)
				{
					dynamicOnDamageModifiers[i].Apply(StatsBehaviour.DynamicStatsMultipliers, enemy);
				}
			}
		}

		public DamageInfo CalculateDamage(BaseEnemyController enemy)
		{
			RejectLegacyExecutionForNativeWeapon();
			EvaluateDynamicOnDamageStatModifiers(enemy);
			int damageValue = DamageValue;
			damageValue = ApplyPlayerConditionDamageMultipliers(damageValue);
			damageValue = ApplyEnemyConditionDamageMultipliers(damageValue, enemy);
			damageValue = ApplyEnemyTypeDamageMultipliers(damageValue, enemy);
			bool flag = false;
			if (CriticalRoll())
			{
				damageValue = (int)((float)damageValue * CritMultiplier);
				flag = true;
			}
			this.OnWeaponDamage?.Invoke(damageValue, flag);
			return new DamageInfo(ID, damageValue, flag);
		}

		private int ApplyPlayerConditionDamageMultipliers(int damageValue)
		{
			if (OwnerCombatant != null &&
				OwnerCombatant.CurrentHealth == OwnerCombatant.MaximumHealth)
			{
				damageValue = (int)((float)damageValue * (1f + StatsBehaviour.PlayerStats.StatMultipliers.attackStatsMultipliers.playerFullHealthMultiplier));
			}
			return damageValue;
		}

		private int ApplyEnemyConditionDamageMultipliers(int damageValue, BaseEnemyController enemy)
		{
			if (enemy.IsAtFullHealth)
			{
				damageValue = (int)((float)damageValue * (1f + StatsBehaviour.PlayerStats.StatMultipliers.attackStatsMultipliers.pristineDamageMultiplier));
			}
			if (enemy.status.HasAnyStatus())
			{
				damageValue = (int)((float)damageValue * (1f + statsBehaviour.PlayerStats.statMultipliers.attackStatsMultipliers.statusGeneralMultiplier));
			}
			return damageValue;
		}

		private int ApplyEnemyTypeDamageMultipliers(int damageValue, BaseEnemyController enemy)
		{
			if (enemy.isElite)
			{
				damageValue = (int)((float)damageValue * (1f + StatsBehaviour.PlayerStats.StatMultipliers.attackStatsMultipliers.eliteDamageMultiplier));
			}
			damageValue = ((!enemy.enemyRanged) ? ((int)((float)damageValue * (1f + StatsBehaviour.PlayerStats.StatMultipliers.attackStatsMultipliers.meleeDamageMultiplier))) : ((int)((float)damageValue * (1f + StatsBehaviour.PlayerStats.StatMultipliers.attackStatsMultipliers.rangedDamageMultiplier))));
			return damageValue;
		}

		private bool CriticalRoll()
		{
			if (UnityEngine.Random.Range(0f, 1f) <= CritRate)
			{
				return true;
			}
			return false;
		}

		private void EnsureCombatExecution()
		{
			if (_combatServiceProvider == null)
			{
				_combatServiceProvider = player.GetComponent<CombatRuntimeServiceProvider>();
				if (_combatServiceProvider == null)
				{
					_combatServiceProvider = player.gameObject.AddComponent<CombatRuntimeServiceProvider>();
				}
			}

			CombatRuntimeServices services = _combatServiceProvider.Services;
			if (_combatExecution == null ||
				!ReferenceEquals(_combatExecution.Services, services))
			{
				_combatExecution = new LegacyCombatExecution(services);
				_currentAttackContext = CombatContext.None;
			}
		}

		private void RejectLegacyExecutionForNativeWeapon()
		{
			if (UsesNativeGasRuntime)
			{
				throw new InvalidOperationException(
					"This weapon is New GAS native. Legacy damage/modifier execution is disabled.");
			}
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
