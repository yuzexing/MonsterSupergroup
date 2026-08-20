using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace AstralShift.HellMaiden.Data.Cards
{
	[CreateAssetMenu(fileName = "New Weapon Data", menuName = "HellMaiden/Data/Cards/Weapon Data")]
	public class WeaponData : CardData
	{
		[Header("Weapon Settings")]
		public WeaponBehaviour WeaponPrefab;

		[Space]
		[SerializeField]
		protected bool isSignature;

		[Space]
		[SerializeField]
		protected UltimateData ultimateData;

		[SerializeField]
		protected AttackStats baseStats;

		[SerializeField]
		protected WeaponRarity rarity;

		public ModifierFlags modifierFlags;

		public bool IsSignature => isSignature;

		public UltimateData UltimateData => ultimateData;

		public AttackStats BaseStats => baseStats;

		public WeaponRarity Rarity => rarity;

		public float GetBaseDamage()
		{
			return BaseStats.damage;
		}

		public float GetBaseSize()
		{
			return BaseStats.size;
		}

		public float GetBaseDuration()
		{
			return BaseStats.duration;
		}

		public float GetBaseSpeed()
		{
			return WeaponPrefab.GetAttacksPerSecond(BaseStats);
		}

		public float GetBaseCritMultiplier()
		{
			return BaseStats.critMultiplier;
		}

		public float GetBaseCritRate()
		{
			return BaseStats.critRate;
		}

		public int GetBaseProjectileCount()
		{
			return BaseStats.projectileCount;
		}

		public float GetBaseKnockbackDistance()
		{
			if (BaseStats.knockbackSettings != null)
			{
				return BaseStats.knockbackSettings.distance;
			}
			return 0f;
		}
	}
}
