using System.Collections.Generic;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.MapGeneration;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline.Progression.Shrines
{
	public class ShrineSpawnerClip : SpritePlayableAsset, ITimelineClipAsset, IProgressionClip
	{
		private ShrineSpawnerBehaviour template = new ShrineSpawnerBehaviour();

		public List<PropAsset> propAssets = new List<PropAsset>();

		[Range(0f, 1f)]
		public float chance;

		public ClipCaps clipCaps => ClipCaps.None;

		public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
		{
			return ScriptPlayable<ShrineSpawnerBehaviour>.Create(graph, template);
		}

		public void ProcessClip(ProgressionTimeline timeline, TimelineClip clip)
		{
			float startTime = (float)clip.start;
			float endTime = (float)clip.end;
			ShrineReplacerRequest propReplacerInfo = new ShrineReplacerRequest(propAssets, chance, startTime, endTime);
			timeline.PropReplacerManagerInstance.AddPropPlacerRequests(propReplacerInfo);
			timeline.CreateMilestone(timeline.PropReplacerManagerInstance, clip);
		}
	}
}
