using System;
using AstralShift.HellMaiden.CameraFX;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	[Serializable]
	public struct AttackStats
	{
		public int damage;

		public float critMultiplier;

		public float critRate;

		[Tooltip("Higher is faster 1/value")]
		public float speed;

		public float size;

		public float duration;

		public int projectileCount;

		public CameraShakeSettings cameraShakeSettings;

		public float cameraShakePerLevelIncrement;

		public KnockbackSettings knockbackSettings;

		public DamageType damageType;

		private AttackStats(string name = null)
		{
			damage = 10;
			critMultiplier = 1.5f;
			critRate = 0.1f;
			speed = 1f;
			size = 1f;
			duration = 1f;
			projectileCount = 1;
			cameraShakeSettings = null;
			cameraShakePerLevelIncrement = 0.1f;
			knockbackSettings = null;
			damageType = DamageType.Normal;
		}
	}
}
