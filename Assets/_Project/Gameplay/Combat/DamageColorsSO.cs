using System;
using AstralShift.HellMaiden.Player.Attacks;
using DamageNumbersPro;
using UnityEngine;

[CreateAssetMenu(fileName = "DamageColorsSO", menuName = "Scriptable Objects/Damage Colors")]
public class DamageColorsSO : ScriptableObject
{
	[Serializable]
	public struct DamageNumberGroup
	{
		public DamageNumber normal;

		public DamageNumber critical;

		public DamageNumber GetDamageNumber(bool isCritical)
		{
			if (isCritical)
			{
				return critical;
			}
			return normal;
		}
	}

	public DamageNumberGroup normalDamage;

	public DamageNumberGroup fireDamage;

	public DamageNumberGroup poisonDamage;

	public DamageNumberGroup bleedDamage;

	public DamageNumberGroup lightningDamage;

	public DamageNumber GetDamageTypeColor(DamageType type, bool isCritical)
	{
		switch (type)
		{
		case DamageType.Normal:
			return normalDamage.GetDamageNumber(isCritical);
		case DamageType.Fire:
			return fireDamage.GetDamageNumber(isCritical);
		case DamageType.Poison:
			return poisonDamage.GetDamageNumber(isCritical);
		case DamageType.Bleed:
			return bleedDamage.GetDamageNumber(isCritical);
		case DamageType.Lightning:
			return lightningDamage.GetDamageNumber(isCritical);
		default:
			Debug.LogWarning("Unrecognized damage type defaulting to normal color");
			return normalDamage.normal;
		}
	}

	public DamageNumber GetRandomDamageTypeColor()
	{
		return GetDamageTypeColor((DamageType)UnityEngine.Random.Range(0, Enum.GetValues(typeof(DamageType)).Length), isCritical: false);
	}
}
