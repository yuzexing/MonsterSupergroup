using System.Collections.Generic;

namespace AstralShift.HellMaiden.MapGeneration
{
	public class ShrineReplacerRequest : PropReplacerRequest
	{
		public ShrineReplacerRequest(List<PropAsset> PropAssets, float chance, float startTime, float endTime)
			: base(PropAssets, chance, startTime, endTime)
		{
			base.PropAssets = PropAssets;
			base.chance = chance;
			base.startTime = startTime;
			base.endTime = endTime;
		}
	}
}
