using System;

[AttributeUsage(AttributeTargets.Class)]
public class PerkModifierTypeAttribute : Attribute
{
	public string DisplayName;

	public PerkModifierTypeAttribute(string displayName = null)
	{
		DisplayName = displayName;
	}
}
