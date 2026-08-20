using System.ComponentModel;
using AstralShift.DebugTools;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline.SlowMotion
{
	[TrackColor(0f, 1f, 0f)]
	[TrackClipType(typeof(SlowMotionClip))]
	[DisplayName("AstralShift/Cutscenes/Slow Motion Track")]
	public class SlowMotionTrack : AstralTrackAsset
	{
		public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
		{
			if (graph.GetTimeUpdateMode() != DirectorUpdateMode.UnscaledGameTime)
			{
				DBL.Log(DBL.Module.Timeline, "ATTENTION! Timeline's Update Method should be set to UnscaledGameTime", 2);
			}
			SetClipsMinimumSize();
			return ScriptPlayable<SlowMotionTrackMixer>.Create(graph, inputCount);
		}
	}
}
