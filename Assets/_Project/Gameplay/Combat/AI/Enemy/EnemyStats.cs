using System;
using AstralShift.Helpers.Attributes;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	[Serializable]
	public class EnemyStats
	{
		[SerializeField]
		protected EnemyStatsValues baseStats;

		[SerializeField]
		[ReadOnly]
		protected EnemyStatsValues currentStats;

		[SerializeField]
		protected EnemyStatsMultipliers multipliers;

		public int BaseHealth
		{
			get
			{
				return baseStats.Health;
			}
			set
			{
				baseStats.Health = value;
				this.OnHealthChanged?.Invoke();
				this.OnStatsChanged?.Invoke();
			}
		}

		public int Health
		{
			get
			{
				return currentStats.Health;
			}
			set
			{
				currentStats.Health = Mathf.RoundToInt((float)value * multipliers.Health);
				this.OnHealthChanged?.Invoke();
				this.OnStatsChanged?.Invoke();
			}
		}

		public float HealthMultiplier
		{
			get
			{
				return (int)multipliers.Health;
			}
			set
			{
				multipliers.Health = value;
				this.OnHealthChanged?.Invoke();
				this.OnStatsChanged?.Invoke();
			}
		}

		public int BaseDamage
		{
			get
			{
				return baseStats.Damage;
			}
			set
			{
				baseStats.Damage = value;
				this.OnDamageChanged?.Invoke();
				this.OnStatsChanged?.Invoke();
			}
		}

		public int Damage
		{
			get
			{
				return (int)((float)currentStats.Damage * multipliers.Damage);
			}
			set
			{
				currentStats.Damage = value;
				this.OnDamageChanged?.Invoke();
				this.OnStatsChanged?.Invoke();
			}
		}

		public float DamageMultiplier
		{
			get
			{
				return multipliers.Damage;
			}
			set
			{
				multipliers.Damage = value;
				this.OnDamageChanged?.Invoke();
				this.OnStatsChanged?.Invoke();
			}
		}

		public float BaseXP
		{
			get
			{
				return baseStats.XP;
			}
			set
			{
				baseStats.XP = value;
				this.OnXPChanged?.Invoke();
				this.OnStatsChanged?.Invoke();
			}
		}

		public float XP
		{
			get
			{
				return currentStats.XP * multipliers.XP;
			}
			set
			{
				currentStats.XP = value;
				this.OnXPChanged?.Invoke();
				this.OnStatsChanged?.Invoke();
			}
		}

		public float XPMultiplier
		{
			get
			{
				return multipliers.XP;
			}
			set
			{
				multipliers.XP = value;
				this.OnXPChanged?.Invoke();
				this.OnStatsChanged?.Invoke();
			}
		}

		public float StunTime
		{
			get
			{
				return currentStats.StunTime;
			}
			set
			{
				currentStats.StunTime = value;
				this.OnStunTimeChanged?.Invoke();
				this.OnStatsChanged?.Invoke();
			}
		}

		public float BaseSpeed
		{
			get
			{
				return baseStats.Speed;
			}
			set
			{
				baseStats.Speed = value;
				this.OnSpeedChanged?.Invoke();
				this.OnStatsChanged?.Invoke();
			}
		}

		public float Speed
		{
			get
			{
				return currentStats.Speed * multipliers.Speed;
			}
			set
			{
				currentStats.Speed = value;
				this.OnSpeedChanged?.Invoke();
				this.OnStatsChanged?.Invoke();
			}
		}

		public float SpeedMultiplier
		{
			get
			{
				return multipliers.Speed;
			}
			set
			{
				multipliers.Speed = value;
				this.OnSpeedChanged?.Invoke();
				this.OnStatsChanged?.Invoke();
			}
		}

		public float KnockBackMultiplier
		{
			get
			{
				return baseStats.KnockBackMultiplier;
			}
			set
			{
				baseStats.KnockBackMultiplier = value;
				this.OnKnockbackChanged?.Invoke();
				this.OnStatsChanged?.Invoke();
			}
		}

		public float WindMultiplier
		{
			get
			{
				return baseStats.WindMultiplier;
			}
			set
			{
				baseStats.WindMultiplier = value;
				this.OnWindMultiplierChanged?.Invoke();
				this.OnStatsChanged?.Invoke();
			}
		}

		public event Action OnStatsChanged;

		public event Action OnHealthChanged;

		public event Action OnDamageChanged;

		public event Action OnXPChanged;

		public event Action OnSpeedChanged;

		public event Action OnKnockbackChanged;

		public event Action OnStunTimeChanged;

		public event Action OnWindMultiplierChanged;

		public void Init(EnemyStatsValues stats)
		{
			baseStats = stats.Clone();
			Reset();
		}

		public void Reset()
		{
			currentStats = baseStats.Clone();
			multipliers = new EnemyStatsMultipliers();
		}

		public EnemyStats Clone()
		{
			return new EnemyStats
			{
				baseStats = baseStats.Clone(),
				currentStats = currentStats.Clone(),
				multipliers = multipliers.Clone()
			};
		}
	}
}
