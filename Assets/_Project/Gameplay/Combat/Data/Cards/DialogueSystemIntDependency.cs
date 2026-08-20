using System;
// using PixelCrushers.DialogueSystem;
using UnityEngine;

namespace AstralShift.HellMaiden.Data.Cards
{
	[Serializable]
	public class DialogueSystemIntDependency : DataDependency
	{
		// [SerializeField]
		// [VariablePopup(false)]
		// private string variable;

		public Comparison comparison;

		public int number;

		// public bool IsDependencyMet => Compare(GameDataManager.GetGameInt(variable));

		private bool Compare(int value)
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
