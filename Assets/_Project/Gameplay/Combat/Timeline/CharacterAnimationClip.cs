using AstralShift.QTI.Helpers.Attributes;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline
{
	public class CharacterAnimationClip : EmoteClip, ITimelineClipAsset
	{
		public CharacterAnimationBehaviour template = new CharacterAnimationBehaviour();

		[Header("Animation")]
		public AnimationClip animation;

		public float animDuration;

		[ConditionalHide("loop", true)]
		public uint loopTimes;

		[HideInInspector]
		public bool loop;

		public int layer;

		public bool blockOtherAnimations = true;

		public float AnimDuration => animDuration * (float)(loopTimes + 1);

		public new ClipCaps clipCaps => ClipCaps.Blending;

		public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
		{
			ScriptPlayable<CharacterAnimationBehaviour> scriptPlayable = ScriptPlayable<CharacterAnimationBehaviour>.Create(graph, template);
			CharacterAnimationBehaviour behaviour = scriptPlayable.GetBehaviour();
			behaviour.animation = animation;
			animDuration = animation.length;
			loop = animation.isLooping;
			behaviour.emoji = emoji;
			behaviour.layer = layer;
			behaviour.blockOtherAnimations = blockOtherAnimations;
			return scriptPlayable;
		}
	}
}
