using UnityEngine;

namespace AstralShift.Helpers.Attributes
{
	public class DisplayNameAttribute : PropertyAttribute
	{
		public string DisplayName { get; }

		public DisplayNameAttribute(string displayName)
		{
			DisplayName = displayName;
		}
	}
}
