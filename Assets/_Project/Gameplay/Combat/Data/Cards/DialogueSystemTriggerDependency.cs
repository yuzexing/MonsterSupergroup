using System;
// using PixelCrushers.DialogueSystem;
using UnityEngine;

namespace AstralShift.HellMaiden.Data.Cards
{
	[Serializable]
	public class DialogueSystemTriggerDependency : DataDependency
	{
		// [SerializeField]
		// [VariablePopup(false)]
		// private string trigger;

		[SerializeField]
		private bool state;

		// public string Trigger => trigger;

		// public bool IsDependencyMet => GameDataManager.GetGameTriggerState(trigger) == state;
	}
}
