using System;
using System.Collections.Generic;
using System.Linq;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Combat.Hand;
using FMODUnity;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public abstract class WeaponBehaviour : MonoBehaviour
	{
		[SerializeField]
		protected WeaponBehaviourStats statsBehaviour;

		protected PlayerMovement player;

		protected uint _id;

		protected RuntimeEquipmentModifiers _equipmentModifiers;

		[Header("Sounds")]
		[SerializeField]
		protected EventReference attackSound;

		public uint ID => _id;

		public RuntimeEquipmentModifiers EquipmentModifiers => _equipmentModifiers;

		public AttackStats BaseAttackStats => StatsBehaviour.BaseStats;

		public AttackStatsMultipliers BaseStatsMultipliers => StatsBehaviour.BaseStatsMultipliers;

		public int DamageValue => StatsBehaviour.DamageValue;

		public float DamageMultiplierSum => StatsBehaviour.DamageMultiplierSum;

		public float SizeValue => StatsBehaviour.SizeValue;

		public float SizeMultiplierSum => StatsBehaviour.SizeMultiplierSum;

		public float SpeedValue => StatsBehaviour.SpeedValue;

		public float SpeedMultiplierSum => StatsBehaviour.SpeedMultiplierSum;

		public float SpeedMultipliersProduct => StatsBehaviour.SpeedMultipliersProduct;

		public float DurationValue => StatsBehaviour.DurationValue;

		public float DurationMultiplierSum => StatsBehaviour.DurationMultiplierSum;

		public int ProjectileCountValue => StatsBehaviour.ProjectileCountValue;

		public float CritRate => StatsBehaviour.CritRate;

		public float CritRateMultiplierSum => StatsBehaviour.CritRateMultiplierSum;

		public float CritMultiplier => StatsBehaviour.CritDamageMultiplier;

		public float CritMultiplierSum => StatsBehaviour.CritDamageMultiplier;

		public float KnockBackDistance => StatsBehaviour.KnockBackDistance;

		public float KnockBackMultiplierSum => StatsBehaviour.KnockBackMultiplierSum;

		public KnockbackSettings KnockbackSettings => StatsBehaviour.BaseStats.knockbackSettings;

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

		public virtual void Init(uint id, AttackStats stats)
		{
			_id = id;
			player = GameDirector.Instance.Player;
			statsBehaviour = new WeaponBehaviourStats(stats, player.PlayerStats);
		}

		protected abstract void Dispose();

		public virtual void Attack()
		{
			EvaluateDynamicStatModifiers();
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
			damageable?.Damage(position, this, StatsBehaviour.BaseStats.damageType);
		}

		public virtual void OnHit(Vector2 position, IDamageable damageable)
		{
			this.OnWeaponHit?.Invoke();
			Damage(position, damageable);
		}

		public virtual void UpdateModifiers(RuntimeEquipmentModifiers runtimeModifiers)
		{
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
			if (dynamicModifiers == null)
			{
				return;
			}
			for (int i = 0; i < dynamicModifiers.Count; i++)
			{
				if (dynamicModifiers[i] != null)
				{
					dynamicModifiers[i].Apply(StatsBehaviour, this);
				}
			}
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
			if (StatsBehaviour.PlayerStats.currentStats.HP == StatsBehaviour.PlayerStats.currentStats.maxHP)
			{
				damageValue = (int)((float)damageValue * (1f + StatsBehaviour.PlayerStats.StatMultipliers.attackStatsMultipliers.playerFullHealthMultiplier));
			}
			return damageValue;
		}

		private int ApplyEnemyConditionDamageMultipliers(int damageValue, BaseEnemyController enemy)
		{
			if (enemy.stats.Health == enemy.stats.BaseHealth)
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

		public void PlayAttackSound()
		{
			if (!attackSound.IsNull)
			{
				RuntimeManager.PlayOneShot(attackSound, base.transform.position);
			}
		}
	}
}
