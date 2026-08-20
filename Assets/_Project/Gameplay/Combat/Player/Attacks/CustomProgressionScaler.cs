using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public abstract class CustomProgressionScaler : MonoBehaviour, IProgressionScaler
	{
		public abstract void Apply(float percentageMultiplier);

		public abstract void SetDefaults();
	}
}
