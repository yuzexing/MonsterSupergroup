using System.ComponentModel;
using AstralShift.HellMaiden.Timeline.TimelineCharacterMovement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline
{
	[TrackColor(0.855f, 0.8623f, 0.87f)]
	[TrackClipType(typeof(TransformTweenClip))]
	[TrackClipType(typeof(CharacterMovementClip))]
	[TrackClipType(typeof(SetDirectionClip))]
	[TrackClipType(typeof(CharacterAnimationClip))]
	[TrackClipType(typeof(AnimationAndTweenClip))]
	[TrackBindingType(typeof(Transform))]
	[DisplayName("AstralShift/Cutscenes/Transform Tween Track")]
	public class TransformTweenTrack : TrackAsset
	{
		public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
		{
			return ScriptPlayable<TransformTweenMixerBehaviour>.Create(graph, inputCount);
		}

		public override void GatherProperties(PlayableDirector director, IPropertyCollector driver)
		{
			base.GatherProperties(director, driver);
		}
	}
}
