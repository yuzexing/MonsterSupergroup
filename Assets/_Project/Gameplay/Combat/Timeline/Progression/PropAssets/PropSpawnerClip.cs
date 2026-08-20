using System.Collections.Generic;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.MapGeneration;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline.Progression.PropAssets
{
	public class PropSpawnerClip : SpritePlayableAsset, IProgressionClip
	{
		private PropSpawnerBehavior template = new PropSpawnerBehavior();

		public List<PropAsset> propsToSpawn;

		[Range(0f, 1f)]
		public float spawnChance;

		public ClipCaps clipCaps => ClipCaps.None;

		public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
		{
			return ScriptPlayable<PropSpawnerBehavior>.Create(graph, template);
		}

		public void ProcessClip(ProgressionTimeline timeline, TimelineClip clip)
		{
			float startTime = (float)clip.start;
			float endTime = (float)clip.end;
			PropReplacerRequest propReplacerInfo = new PropReplacerRequest(propsToSpawn, spawnChance, startTime, endTime);
			timeline.PropReplacerManagerInstance.AddPropPlacerRequests(propReplacerInfo);
			timeline.CreateMilestone(timeline.PropReplacerManagerInstance, clip);
		}
	}
}
