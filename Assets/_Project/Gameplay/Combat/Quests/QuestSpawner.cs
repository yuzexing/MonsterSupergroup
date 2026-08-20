using AstralShift.HellMaiden.Combat;

namespace AstralShift.HellMaiden.Quests
{
	public class QuestSpawner : SerializedProgressable
	{
		public DivinaQuestGoal Quest { get; set; }

		public override void Init()
		{
			Quest.Init();
			base.hasEnded = true;
		}

		public override void ProgressUpdate()
		{
		}

		public override void End()
		{
		}
	}
}
