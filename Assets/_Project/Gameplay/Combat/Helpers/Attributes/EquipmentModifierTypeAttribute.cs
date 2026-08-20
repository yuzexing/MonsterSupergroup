using System;

[AttributeUsage(AttributeTargets.Class)]
public class EquipmentModifierTypeAttribute : Attribute
{
	public string DisplayName;

	public EquipmentModifierTypeAttribute(string displayName = null)
	{
		DisplayName = displayName;
	}
}
