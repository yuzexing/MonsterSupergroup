using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline
{
	public class AnimationAndTweenClip : TransformTweenClip
	{
		public AnimationAndTweenBehaviour animationAndTweenTemplate = new AnimationAndTweenBehaviour();

		[Header("Animation")]
		public AnimationClip animation;

		public new ClipCaps clipCaps => ClipCaps.Blending;

		public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
		{
			ScriptPlayable<AnimationAndTweenBehaviour> scriptPlayable = ScriptPlayable<AnimationAndTweenBehaviour>.Create(graph, animationAndTweenTemplate);
			AnimationAndTweenBehaviour behaviour = scriptPlayable.GetBehaviour();
			behaviour.startLocation = startLocation.Resolve(graph.GetResolver());
			behaviour.endLocation = endLocation.Resolve(graph.GetResolver());
			behaviour.animation = animation;
			SetPlayableEmoji(behaviour);
			return scriptPlayable;
		}
	}
}
