using UnityEngine;
using UnityEngine.Playables;

namespace AstralShift.Cinematics.Timeline
{
	public class TimelineSubtitleClip : PlayableAsset
	{
		[SerializeField]
		private string term;

		[SerializeField]
		private bool useI2 = true;

		[SerializeField]
		private bool overridePosition;

		[SerializeField]
		private Vector2 position;

		public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
		{
			ScriptPlayable<TimelineSubtitleBehaviour> scriptPlayable = ScriptPlayable<TimelineSubtitleBehaviour>.Create(graph);
			TimelineSubtitleBehaviour behaviour = scriptPlayable.GetBehaviour();
			if (useI2)
			{
				behaviour.SetTranslatedText(term);
			}
			else
			{
				behaviour.SetText(term);
			}
			behaviour.SetPosition(overridePosition ? position : Vector2.zero);
			return scriptPlayable;
		}
	}
}
