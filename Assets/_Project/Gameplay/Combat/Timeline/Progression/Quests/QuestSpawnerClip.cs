using AstralShift.HellMaiden.Quests;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline.Progression.Quests
{
	public class QuestSpawnerClip : SpritePlayableAsset, ITimelineClipAsset
	{
		private QuestSpawnerBehaviour template = new QuestSpawnerBehaviour();

		public DivinaQuestGoal quest;

		public ClipCaps clipCaps => ClipCaps.None;

		public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
		{
			return ScriptPlayable<QuestSpawnerBehaviour>.Create(graph, template);
		}
	}
}
