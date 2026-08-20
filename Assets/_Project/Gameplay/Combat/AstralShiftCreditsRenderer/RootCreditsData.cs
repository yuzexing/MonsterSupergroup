using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace AstralShift.AstralShiftCreditsRenderer
{
	[Serializable]
	public class RootCreditsData
	{
		[JsonProperty("Sessions")]
		public List<Session> Sessions { get; set; }
	}
}
