using System;

namespace AstralShift.HellMaiden.Data
{
	[Flags]
	public enum ModifierFlags
	{
		None = 0,
		Damage = 1,
		Size = 2,
		Speed = 4,
		Duration = 8,
		ProjectileCount = 0x10,
		CritRate = 0x20,
		CritDamage = 0x40,
		KnockBack = 0x80
	}
}
