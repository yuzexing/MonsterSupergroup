using System;
using AstralShift.HellMaiden.CameraFX;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace AstralShift.HellMaiden.Data.Cards
{
	[Serializable]
	public sealed class WeaponPresentationSettings
	{
		[SerializeField]
		private CameraShakeSettings cameraShake;

		[SerializeField]
		private float cameraShakePerLevelIncrement = 0.1f;

		[SerializeField]
		private KnockbackSettings knockback;

		public CameraShakeSettings CameraShake => cameraShake;

		public float CameraShakePerLevelIncrement => cameraShakePerLevelIncrement;

		public KnockbackSettings Knockback => knockback;

		public void Configure(
			CameraShakeSettings newCameraShake,
			float newCameraShakePerLevelIncrement,
			KnockbackSettings newKnockback)
		{
			cameraShake = newCameraShake;
			cameraShakePerLevelIncrement = newCameraShakePerLevelIncrement;
			knockback = newKnockback;
		}
	}
}
