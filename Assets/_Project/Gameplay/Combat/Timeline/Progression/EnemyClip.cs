using AstralShift.HellMaiden.Combat;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline.Progression
{
	public abstract class EnemyClip : SpritePlayableAsset, IProgressionClip
	{
		public int variantIndex;

		public abstract void ProcessClip(ProgressionTimeline timeline, TimelineClip clip);
	}
}
