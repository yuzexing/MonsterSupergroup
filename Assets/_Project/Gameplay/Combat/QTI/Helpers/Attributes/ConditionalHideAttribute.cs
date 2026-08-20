using UnityEngine;

namespace AstralShift.QTI.Helpers.Attributes
{
	public class ConditionalHideAttribute : PropertyAttribute
	{
		public string ConditionalSourceField;

		public bool HideIfFalse;

		public ConditionalHideAttribute(string conditionalSourceField, bool hideIfFalse = true)
		{
			ConditionalSourceField = conditionalSourceField;
			HideIfFalse = hideIfFalse;
		}
	}
}
