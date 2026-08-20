namespace AstralShift.HellMaiden.Combat.Hand
{
	public abstract class RuntimePerkModifier
	{
		private uint _id;

		public uint ID
		{
			get
			{
				return _id;
			}
			set
			{
				_id = value;
			}
		}

		public abstract bool TryStack(RuntimePerkModifier other);
	}
}
