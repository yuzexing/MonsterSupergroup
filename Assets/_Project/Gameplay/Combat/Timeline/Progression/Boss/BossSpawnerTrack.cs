using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using AstralShift.DebugTools;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Events;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline.Progression.Boss
{
	[TrackColor(0.8f, 0.9f, 0f)]
	[TrackClipType(typeof(BossSpawnerClip))]
	[DisplayName("AstralShift/Progression/Boss Spawner Track")]
	public class BossSpawnerTrack : TrackAsset, IProgressionTrack
	{
		public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
		{
			return ScriptPlayable<BossSpawnerTrackMixer>.Create(graph, inputCount);
		}

		public void ProcessTrack(ProgressionTimeline timeline)
		{
			if (!timeline.IsTimeoutEnabled)
			{
				return;
			}
			float num = timeline.EndTime;
			IEnumerable<TimelineClip> source = GetClips();
			if (source.Any())
			{
				num = (float)source.Min((TimelineClip timelineClip) => timelineClip.start);
			}
			CircleTimeoutProgressionEvent circleTimeoutProgressionEvent = new CircleTimeoutProgressionEvent();
			float num2 = Mathf.Clamp(num - timeline.TimeoutDuration, 0f, timeline.EndTime);
			circleTimeoutProgressionEvent.countdownDuration = num - num2;
			timeline.CreateMilestone(circleTimeoutProgressionEvent, num2, num);
			DBL.Log(DBL.Module.ProgressionTimeline, $"Circle Timeout Event Set: Warning starts at {num2}s, Boss arrives at {num}s");
		}
	}
}
