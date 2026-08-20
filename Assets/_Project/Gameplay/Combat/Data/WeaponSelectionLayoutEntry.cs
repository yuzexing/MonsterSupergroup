using System;
using AstralShift.HellMaiden.Data.Cards;
using UnityEngine;

namespace AstralShift.HellMaiden.Data
{
	[Serializable]
	public class WeaponSelectionLayoutEntry
	{
		[SerializeField]
		private WeaponData weaponData;

		[SerializeField]
		private WSMWeapon3DView weapon3DViewPrefab;

		[SerializeField]
		private GameObject weaponHubView;

		[SerializeField]
		private Material wsmLightRayInnerMaterial;

		[SerializeField]
		private Material wsmLightRayOuterMaterial;

		[SerializeField]
		private Material wsmFrameGemsMaterial;

		public WeaponData WeaponData => weaponData;

		public WSMWeapon3DView Weapon3DViewPrefab => weapon3DViewPrefab;

		public GameObject WeaponHubView => weaponHubView;

		public Material WSMLightRayInnerMaterial => wsmLightRayInnerMaterial;

		public Material WSMLightRayOuterMaterial => wsmLightRayOuterMaterial;

		public Material WSMFrameGemsMaterial => wsmFrameGemsMaterial;

		private bool IsSignature
		{
			get
			{
				if (weaponData != null)
				{
					return weaponData.IsSignature;
				}
				return false;
			}
		}
	}
}
