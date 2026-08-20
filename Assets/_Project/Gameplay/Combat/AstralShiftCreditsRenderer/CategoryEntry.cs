using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace AstralShift.AstralShiftCreditsRenderer
{
	[Serializable]
	public class CategoryEntry
	{
		[JsonProperty("Title")]
		public string Title { get; set; }

		[JsonProperty("Position")]
		public string Position { get; set; }

		[JsonProperty("CreditEntries")]
		public List<CreditEntry> CreditEntries { get; set; }
	}
}
