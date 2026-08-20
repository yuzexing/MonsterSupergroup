using Newtonsoft.Json;

namespace AstralShift.HellMaiden.ProfileData
{
	public class ProfileData
	{
		private static ProfileData instance;

		public static ProfileData Instance
		{
			get
			{
				if (instance == null)
				{
					instance = new ProfileData();
				}
				return instance;
			}
			set
			{
				instance = value;
			}
		}

		[JsonProperty]
		public float TotalGameTime { get; set; }

		public ProfileData()
		{
			TotalGameTime = 0f;
		}
	}
}
