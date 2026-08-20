using System.Collections.Generic;
using AstralShift.HellMaiden.Data.Cards;
using UnityEngine;

namespace AstralShift.HellMaiden.Data
{
	[CreateAssetMenu(fileName = "WeaponSelectionLayouts", menuName = "Scriptable Objects/Weapon Selection Layouts")]
	public class WeaponSelectionLayouts : ScriptableObject
	{
		[SerializeField]
		private List<WeaponSelectionLayoutEntry> weapons = new List<WeaponSelectionLayoutEntry>();

		public bool TryGetEntry(WeaponData weaponData, out WeaponSelectionLayoutEntry entry)
		{
			entry = weapons.Find((WeaponSelectionLayoutEntry x) => x.WeaponData == weaponData);
			return entry != null;
		}

		public bool TryGetEntryByID(int weaponID, out WeaponSelectionLayoutEntry entry)
		{
			entry = weapons.Find((WeaponSelectionLayoutEntry x) => x.WeaponData != null && x.WeaponData.ID == weaponID);
			return entry != null;
		}
	}
}
