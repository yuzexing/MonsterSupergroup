using System.ComponentModel;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline.CameraZoom
{
	[TrackColor(1f, 0f, 0.3403339f)]
	[TrackClipType(typeof(CameraZoomClip))]
	[DisplayName("AstralShift/Cutscenes/Camera Zoom Track")]
	public class CameraZoomTrack : AstralTrackAsset
	{
		public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
		{
			SetClipsMinimumSize();
			return ScriptPlayable<CameraZoomTrackMixer>.Create(graph, inputCount);
		}
	}
}
