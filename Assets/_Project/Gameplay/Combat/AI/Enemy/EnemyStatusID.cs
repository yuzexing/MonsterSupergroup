using System;

namespace AstralShift.HellMaiden.AI.Enemy
{
	[Flags]
	public enum EnemyStatusID
	{
		None = 0,
		Slow = 1,
		Burn = 2,
		Poison = 4,
		Bleed = 8,
		Weaken = 0x10,
		Fragile = 0x20
	}
}
