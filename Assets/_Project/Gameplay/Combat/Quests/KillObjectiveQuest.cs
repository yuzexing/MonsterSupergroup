// using Assets.Scripts.AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Traps;
using AstralShift.HellMaiden.UI;
using Cysharp.Threading.Tasks;
using PixelCrushers.DialogueSystem;
using UnityEngine;
using UnityEngine.Serialization;

namespace AstralShift.HellMaiden.Quests
{
	public class KillObjectiveQuest : MonoBehaviour
	{
		[SerializeField]
		private BarrierTrap barrier;

		public GameObject objective;

		private bool objectiveKilled;

		public GameObject subjectHpBar;

		public EnemyController enemyController;

		public EnemyHurtbox enemyHurtbox;

		// public ShootingPatternHandler shootingPatternHandler;

		public ProgressionTimeline progressionTimeline;

		[SerializeField]
		private GameObject questFinisheddialogueTrigger;

		[SerializeField]
		private bool intercom;

		// [ConversationPopup(false, false)]
		// [SerializeField]
		// private string questIntercomConversation;

		[FormerlySerializedAs("entryID")]
		[Tooltip("Dialogue entry to jump to.")]
		public int questIntercomEntryID;

		public DivinaQuestGoal QuestGoal { get; set; }

		public bool PreQuestIntercom => intercom;

		public void StartQuest()
		{
			barrier.target = objective.transform;
			barrier.Init();
			if (QuestGoal.hasTimeout)
			{
				QuestGoal.StopQuestTimeout();
			}
			progressionTimeline.Init();
			progressionTimeline.StartProgression(0f, 1f);
			enemyController.Target = GameDirector.Instance.Player.transform;
			enemyController.Init(EnemyFactory.GenerateId("OvidCocoon"));
			enemyController.gameObject.SetActive(value: true);
			enemyController.OnConfirmedKill += delegate
			{
				OnObjectiveKilled();
			};
			// IntercomManager.Instance.LaunchIntercom(questIntercomConversation, questIntercomEntryID, null, IntercomManager.MAX_PRIORITY).Forget();
			barrier.onSpawnFinished = delegate
			{
				// shootingPatternHandler.StartShooting();
				enemyHurtbox.gameObject.SetActive(value: true);
				subjectHpBar.gameObject.SetActive(value: true);
			};
		}

		private void OnObjectiveKilled()
		{
			barrier.Stop();
			QuestGoal.Complete();
			subjectHpBar.SetActive(value: false);
			// shootingPatternHandler.StopShooting();
			progressionTimeline.KillAllEnemies();
			progressionTimeline.Pause();
			progressionTimeline.EndTimeline();
			if (questFinisheddialogueTrigger != null)
			{
				questFinisheddialogueTrigger.gameObject.SetActive(value: true);
			}
		}
	}
}
