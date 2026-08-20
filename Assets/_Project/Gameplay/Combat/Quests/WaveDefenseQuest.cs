using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Traps;
using UnityEngine;

namespace AstralShift.HellMaiden.Quests
{
	public class WaveDefenseQuest : MonoBehaviour
	{
		[SerializeField]
		private EnemyDamageableObject horaceHitBox;

		[SerializeField]
		private Transform target;

		[SerializeField]
		private BarrierTrap barrier;

		public DivinaQuestGoal quest;

		public GameObject subjectHpBar;

		public ProgressionTimeline progressionTimeline;

		[SerializeField]
		private GameObject enemiesHudIndicator;

		[SerializeField]
		private GameObject questFinisheddialogueTrigger;

		public void StartWaveDefense()
		{
			subjectHpBar.SetActive(value: true);
			horaceHitBox.isImmortal = false;
			barrier.target = target.transform;
			barrier.Init();
			progressionTimeline.enemyContinuousWaveDefenceSpawner = Object.Instantiate(progressionTimeline.enemyContinuousWaveDefenceSpawner, base.transform);
			progressionTimeline.enemyContinuousWaveDefenceSpawner.hudIndicator = enemiesHudIndicator;
			progressionTimeline.enemyContinuousWaveDefenceSpawner.target = target;
			progressionTimeline.Init();
			progressionTimeline.OnTimelineEnd += WaveDefenseFinished;
			progressionTimeline.StartProgression(0f, 1f);
			horaceHitBox.OnKilled = QuestFailed;
		}

		private void WaveDefenseFinished()
		{
			barrier.Stop();
			horaceHitBox.OnKilled = null;
			horaceHitBox.HideHealthbar();
			horaceHitBox.isImmortal = true;
			questFinisheddialogueTrigger.gameObject.SetActive(value: true);
			subjectHpBar.SetActive(value: false);
		}

		public void QuestCompleted()
		{
			quest.Complete();
		}

		private void QuestFailed()
		{
			progressionTimeline.OnTimelineEnd -= WaveDefenseFinished;
			progressionTimeline.KillAllEnemies();
			progressionTimeline.Pause();
			progressionTimeline.EndTimeline();
			barrier.Stop();
			horaceHitBox.HideHealthbar();
			horaceHitBox.transform.parent.gameObject.SetActive(value: false);
			quest.FailQuest();
		}
	}
}
