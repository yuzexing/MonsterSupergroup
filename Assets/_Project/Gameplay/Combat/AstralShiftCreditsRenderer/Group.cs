using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace AstralShift.AstralShiftCreditsRenderer
{
	[Serializable]
	public class Group
	{
		[JsonProperty("AlignmentType")]
		public string AlignmentType { get; set; }

		[JsonProperty("CategoryEntries")]
		public List<CategoryEntry> CategoryEntries { get; set; }
	}
}
