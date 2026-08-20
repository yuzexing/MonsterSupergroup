using AstralShift.HellMaiden.Characters.Effects;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline
{
	public class EmoteClip : PlayableAsset, ITimelineClipAsset
	{
		public EmoteBehaviour emoteTemplate = new EmoteBehaviour();

		[Header("Emote")]
		public CharacterBalloonController.EmojiType emoji;

		public ClipCaps clipCaps => ClipCaps.None;

		public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
		{
			ScriptPlayable<EmoteBehaviour> scriptPlayable = ScriptPlayable<EmoteBehaviour>.Create(graph, emoteTemplate);
			EmoteBehaviour behaviour = scriptPlayable.GetBehaviour();
			SetPlayableEmoji(behaviour);
			return scriptPlayable;
		}

		public void SetPlayableEmoji(EmoteBehaviour emoteBehaviour)
		{
			emoteBehaviour.emoji = emoji;
		}
	}
}
