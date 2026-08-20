using System;
using AstralShift.HellMaiden.Timeline.TransformTween;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline
{
	[Serializable]
	public class TransformTweenClip : EmoteClip, ITimelineClipAsset
	{
		public TransformTweenBehaviour template = new TransformTweenBehaviour();

		[Header("Transform Tween")]
		public ExposedReference<Transform> startLocation;

		public ExposedReference<Transform> endLocation;

		public new ClipCaps clipCaps => ClipCaps.Blending;

		public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
		{
			ScriptPlayable<TransformTweenBehaviour> scriptPlayable = ScriptPlayable<TransformTweenBehaviour>.Create(graph, template);
			TransformTweenBehaviour behaviour = scriptPlayable.GetBehaviour();
			behaviour.startLocation = startLocation.Resolve(graph.GetResolver());
			behaviour.endLocation = endLocation.Resolve(graph.GetResolver());
			SetPlayableEmoji(behaviour);
			return scriptPlayable;
		}
	}
}
