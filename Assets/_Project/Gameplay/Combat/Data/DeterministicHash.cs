using System.Text;

namespace AstralShift.HellMaiden.Data
{
	public static class DeterministicHash
	{
		public static uint Apply(string s)
		{
			if (s == null)
			{
				return 0u;
			}
			uint num = 2166136261u;
			byte[] bytes = Encoding.UTF8.GetBytes(s);
			foreach (byte b in bytes)
			{
				num ^= b;
				num *= 16777619;
			}
			return num;
		}
	}
}
