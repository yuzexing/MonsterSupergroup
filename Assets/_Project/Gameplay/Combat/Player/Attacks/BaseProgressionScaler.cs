using System;

namespace AstralShift.HellMaiden.Player.Attacks
{
	[Serializable]
	public abstract class BaseProgressionScaler : IProgressionScaler
	{
		protected float percentageMultiplier;

		public abstract void Apply(float percentageMultiplier);
	}
}
