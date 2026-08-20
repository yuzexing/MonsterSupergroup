using System;
// using PixelCrushers.DialogueSystem;

namespace AstralShift.HellMaiden.Data
{
	[Serializable]
	public struct DialogueLUTNumberDependency
	{
		public enum Comparison
		{
			Equal = 0,
			Greater = 1,
			Smaller = 2,
			GreaterOrEqual = 3,
			SmallerOrEqual = 4,
			NotEqual = 5
		}

		// [VariablePopup(false)]
		// public string variable;

		public Comparison comparison;

		public int number;

		public bool Compare(int value)
		{
			return comparison switch
			{
				Comparison.Equal => value == number, 
				Comparison.Greater => value > number, 
				Comparison.Smaller => value < number, 
				Comparison.GreaterOrEqual => value >= number, 
				Comparison.SmallerOrEqual => value <= number, 
				Comparison.NotEqual => value != number, 
				_ => false, 
			};
		}
	}
}
