using AstralShift.HellMaiden.Common;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline
{
	public class SetDirectionClip : EmoteClip, ITimelineClipAsset
	{
		public SetDirectionBehaviour template = new SetDirectionBehaviour();

		[Header("Set Direction")]
		public Direction directionToFace;

		public new ClipCaps clipCaps => ClipCaps.Blending;

		public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
		{
			ScriptPlayable<SetDirectionBehaviour> scriptPlayable = ScriptPlayable<SetDirectionBehaviour>.Create(graph, template);
			SetDirectionBehaviour behaviour = scriptPlayable.GetBehaviour();
			behaviour.directionToFace = directionToFace;
			behaviour.duration = duration;
			SetPlayableEmoji(behaviour);
			return scriptPlayable;
		}
	}
}
