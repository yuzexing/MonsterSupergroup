using UnityEngine;

namespace AstralShift.HellMaiden.Data.Cards
{
	public class LevelDependency : DataDependency
	{
		[SerializeField]
		private int level;

		[SerializeField]
		private Comparison comparison;

		public bool IsDependencyMet => Compare(GameDirector.Instance.Player.leveler.Level);

		private bool Compare(int value)
		{
			return comparison switch
			{
				Comparison.Equal => value == level, 
				Comparison.Greater => value > level, 
				Comparison.Smaller => value < level, 
				Comparison.GreaterOrEqual => value >= level, 
				Comparison.SmallerOrEqual => value <= level, 
				Comparison.NotEqual => value != level, 
				_ => false, 
			};
		}
	}
}
