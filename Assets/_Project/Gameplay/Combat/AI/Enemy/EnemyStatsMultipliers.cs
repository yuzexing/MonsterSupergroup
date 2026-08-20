using System;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	[Serializable]
	public class EnemyStatsMultipliers
	{
		[SerializeField]
		protected float hpMultiplier = 1f;

		[SerializeField]
		protected float damageMultiplier = 1f;

		[SerializeField]
		protected float xpMultiplier = 1f;

		[SerializeField]
		protected float speedMultiplier = 1f;

		public float Health
		{
			get
			{
				return hpMultiplier;
			}
			set
			{
				hpMultiplier = value;
			}
		}

		public float Damage
		{
			get
			{
				return damageMultiplier;
			}
			set
			{
				damageMultiplier = value;
			}
		}

		public float XP
		{
			get
			{
				return xpMultiplier;
			}
			set
			{
				xpMultiplier = value;
			}
		}

		public float Speed
		{
			get
			{
				return speedMultiplier;
			}
			set
			{
				speedMultiplier = value;
			}
		}

		public void Reset()
		{
			hpMultiplier = 1f;
			damageMultiplier = 1f;
			xpMultiplier = 1f;
			speedMultiplier = 1f;
		}

		public EnemyStatsMultipliers Clone()
		{
			return new EnemyStatsMultipliers
			{
				Health = Health,
				Damage = Damage,
				XP = XP,
				Speed = Speed
			};
		}
	}
}
