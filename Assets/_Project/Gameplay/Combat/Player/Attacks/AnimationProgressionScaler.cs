using System;

namespace AstralShift.HellMaiden.Player.Attacks
{
	[Serializable]
	public class AnimationProgressionScaler : BaseProgressionScaler
	{
		public AnimatedAttack animatedAttack;

		public float value = 1f;

		public float defaultValue = 1f;

		public override void Apply(float percentageMultiplier)
		{
			if (!(animatedAttack == null))
			{
				base.percentageMultiplier = percentageMultiplier;
			}
		}
	}
}
