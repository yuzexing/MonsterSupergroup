using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace AstralShift.AstralShiftCreditsRenderer
{
	[Serializable]
	public class CreditEntry
	{
		[JsonProperty("Role")]
		public string Role { get; set; }

		[JsonProperty("Names")]
		public List<string> Names { get; set; }
	}
}
