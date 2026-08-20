namespace AstralShift.HellMaiden.Player.Attacks
{
	public struct DamageInfo
	{
		public uint id;

		public int value;

		public bool isCritical;

		public DamageInfo(uint id, int value, bool isCritical)
		{
			this.id = id;
			this.value = value;
			this.isCritical = isCritical;
		}
	}
}
