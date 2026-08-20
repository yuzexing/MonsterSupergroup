using AstralShift.FadeEffect;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline
{
	public class FadeEffectClip : PlayableAsset, ITimelineClipAsset
	{
		public enum FadeClipType
		{
			FadeIn = 0,
			FadeOut = 1
		}

		[HideInInspector]
		public FadeEffectBehaviour template = new FadeEffectBehaviour();

		public FadeClipType fadeType;

		public FadeEffectEnum fadeEffect;

		public ClipCaps clipCaps => ClipCaps.Blending;

		public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
		{
			ScriptPlayable<FadeEffectBehaviour> scriptPlayable = ScriptPlayable<FadeEffectBehaviour>.Create(graph, template);
			FadeEffectBehaviour behaviour = scriptPlayable.GetBehaviour();
			behaviour.fadeEffect = fadeEffect;
			behaviour.fadeType = fadeType;
			return scriptPlayable;
		}
	}
}
