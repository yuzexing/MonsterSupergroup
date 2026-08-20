using System.Collections.Generic;

namespace AstralShift.HellMaiden.MapGeneration
{
	public class PropReplacerRequest
	{
		public int requestId;

		public List<PropAsset> PropAssets;

		public float chance;

		public float startTime;

		public float endTime;

		public PropReplacerRequest(List<PropAsset> PropAssets, float chance, float startTime, float endTime)
		{
			this.PropAssets = PropAssets;
			this.chance = chance;
			this.startTime = startTime;
			this.endTime = endTime;
		}
	}
}
