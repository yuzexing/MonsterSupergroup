using System;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	[Serializable]
	public class EnemyStatsValues
	{
		[SerializeField]
		protected int hp;

		[SerializeField]
		protected int damage;

		[SerializeField]
		protected float xp;

		[SerializeField]
		protected float speed;

		[SerializeField]
		protected float knockbackMultiplier;

		[SerializeField]
		protected float stunTime;

		[SerializeField]
		protected float windMultiplier;

		public int Health
		{
			get
			{
				return hp;
			}
			set
			{
				hp = value;
			}
		}

		public int Damage
		{
			get
			{
				return damage;
			}
			set
			{
				damage = value;
			}
		}

		public float XP
		{
			get
			{
				return xp;
			}
			set
			{
				xp = value;
			}
		}

		public float Speed
		{
			get
			{
				return speed;
			}
			set
			{
				speed = value;
			}
		}

		public float KnockBackMultiplier
		{
			get
			{
				return knockbackMultiplier;
			}
			set
			{
				knockbackMultiplier = value;
			}
		}

		public float StunTime
		{
			get
			{
				return stunTime;
			}
			set
			{
				stunTime = value;
			}
		}

		public float WindMultiplier
		{
			get
			{
				return windMultiplier;
			}
			set
			{
				windMultiplier = value;
			}
		}

		public EnemyStatsValues Clone()
		{
			return new EnemyStatsValues
			{
				Health = Health,
				Damage = Damage,
				XP = XP,
				Speed = Speed,
				KnockBackMultiplier = KnockBackMultiplier,
				StunTime = StunTime,
				WindMultiplier = WindMultiplier
			};
		}
	}
}
