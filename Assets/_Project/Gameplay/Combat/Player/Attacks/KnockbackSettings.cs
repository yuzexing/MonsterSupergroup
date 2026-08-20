using System;
using AstralShift.QTI.Helpers.Attributes;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	[Serializable]
	[CreateAssetMenu(menuName = "HellMaiden/Data/KnockBack Preset")]
	public class KnockbackSettings : ScriptableObject
	{
		public AnimationCurve speedCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

		public float speedMultiplier;

		public float distance;

		public bool fixedDirection;

		[ConditionalHide("fixedDirection", true)]
		public Vector2 direction;

		public float staggerTime;

		public bool HasKnockback => distance > 0f;

		public bool Staggers => staggerTime > 0f;
	}
}
