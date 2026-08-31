	using System;
	using UnityEngine;

namespace AstralShift.HellMaiden.Data.Cards
{
	[CreateAssetMenu(fileName = "WeaponDB", menuName = "Scriptable Objects/WeaponDB")]
	public class WeaponDB : ScriptableObject
	{
		[SerializeField]
		private WeaponData[] weapons;

		[SerializeField]
		protected WeaponTemplateVisualDataLUT visualDataTemplatesLUT;

		public WeaponData[] Weapons => weapons;

		public WeaponTemplateVisualDataLUT VisualDataTemplatesLUT => visualDataTemplatesLUT;

		public void Configure(WeaponData[] entries)
		{
			weapons = entries != null
				? (WeaponData[])entries.Clone()
				: throw new ArgumentNullException(nameof(entries));
		}
	}
}
