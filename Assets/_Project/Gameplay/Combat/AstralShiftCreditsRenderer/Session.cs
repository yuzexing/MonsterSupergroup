using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace AstralShift.AstralShiftCreditsRenderer
{
	[Serializable]
	public class Session
	{
		[JsonProperty("Title")]
		public string Title { get; set; }

		[JsonProperty("Groups")]
		public List<Group> Groups { get; set; }
	}
}
