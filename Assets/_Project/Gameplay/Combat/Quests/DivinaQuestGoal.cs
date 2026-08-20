using System;
using System.Collections;
using System.Collections.Generic;
using AstralShift.HellMaiden.CameraFX;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.GameStats;
using AstralShift.HellMaiden.MapGeneration;
using AstralShift.HellMaiden.UI;
using AstralShift.HellMaiden.UI.Quests;
using AstralShift.Initialization.Verification;
using AstralShift.QTI.Helpers.Attributes;
using Cysharp.Threading.Tasks;
using PixelCrushers.DialogueSystem;
using UnityEngine;
using UnityEngine.Serialization;

namespace AstralShift.HellMaiden.Quests
{
	public class DivinaQuestGoal : MonoBehaviour
	{
		public enum FailReason
		{
			Lost = 0,
			Timeout = 1,
			MainQuestFail = 2
		}

		// [QuestPopup(false)]
		// public string questID;

		public DivinaQuestGoal parentQuest;

		[SerializeField]
		protected Sprite questIcon;

		private bool _completed;

		// public CustomQuestTrackerTemplate questTracker;

		[FormerlySerializedAs("animateIcon")]
		[SerializeField]
		protected bool createMinimapIcon;

		public Action OnComplete;

		public Action OnFail;

		public DivinaQuestGoal[] subGoals;

		[SerializeField]
		private bool _isMainQuest;

		private int subGoalId;

		public bool hasSpecificTile;

		[ConditionalHide("hasSpecificTile", true)]
		public TileGenerator tile;

		[ConditionalHide("hasSpecificTile", true)]
		public uint distanceToPlayer = 1u;

		[ConditionalHide("hasSpecificTile", true)]
		[Tooltip("If false, distanceToPlayer must be exactly the value. If true, it can be greater or equal to the value.")]
		public bool greaterOrEquals = true;

		[ConditionalHide("hasSpecificTile", true)]
		public bool reUseTile;

		[ConditionalHide("hasSpecificTile", true)]
		public bool transitionsAutomatically;

		public QuestArrowPointer2D pointer;

		[Tooltip("If enabled will show an intercom prior to starting a quest")]
		[SerializeField]
		private bool preQuestIntercom;

		// [ConversationPopup(false, false)]
		// [SerializeField]
		// private string preQuestIntercomConversation;

		[FormerlySerializedAs("entryID")]
		[Tooltip("Dialogue entry to jump to.")]
		public int questIntercomEntryID;

		[Tooltip("if enabled will start the quest immediatly anyway but will still delay the quest log tracking")]
		public bool startImmediatly;

		private bool _delayTracking;

		[SerializeField]
		private bool questFailedIntercom;

		// [ConversationPopup(false, false)]
		// [SerializeField]
		// private string questFailedIntercomConversation;

		[Tooltip("Dialogue entry to jump to.")]
		public int questFailedEntryID;

		public EventVerifier eventVerifier;

		public bool hasTimeout;

		[ConditionalHide("hasTimeout", true)]
		public float timeout;

		protected const float timeoutWaitTime = 0.5f;

		protected float _questStartTime;

		protected Coroutine timeoutCoroutine;

		[SerializeField]
		private bool showQuestTracker = true;

		public int _currentGoal { get; protected set; } = 1;

		private bool _hasSubGoals => subGoals.Length != 0;

		public bool IsMainQuest => _isMainQuest;

		public Transform interactionParent { get; protected set; }

		public bool PreQuestIntercom => preQuestIntercom;

		public FailReason questFailedReason { get; protected set; }

		public QuestState questState
		{
			get
			{
				// if (_isMainQuest)
				// {
				// 	return QuestLog.GetQuestState(questID);
				// }
				// return QuestLog.GetQuestEntryState(questID, subGoalId);
				return QuestState.Abandoned;
			}
		}

		public event Action QuestStateChanged;

		public virtual void Init()
		{
			if (IsQuestValid())
			{
				if (preQuestIntercom)
				{
					PreQuestDialogue();
				}
				else
				{
					StartQuest();
				}
			}
		}

		protected virtual void PreQuestDialogue()
		{
			if (startImmediatly)
			{
				// IntercomManager.Instance.LaunchIntercom(preQuestIntercomConversation, questIntercomEntryID, TrackQuest, IntercomManager.MAX_PRIORITY).Forget();
				_delayTracking = true;
				StartQuest();
			}
			else
			{
				// IntercomManager.Instance.LaunchIntercom(preQuestIntercomConversation, questIntercomEntryID, StartQuest, IntercomManager.MAX_PRIORITY).Forget();
			}
		}

		protected virtual void StartQuest()
		{
			// questTracker = DialogueManager.Instance.GetComponentInChildren<CustomQuestTrackerTemplate>();
			if (_isMainQuest)
			{
				for (int i = 0; i < subGoals.Length; i++)
				{
					// QuestLog.SetQuestEntryState(questID, i + 1, QuestState.Unassigned);
					// subGoals[i].questID = questID;
					DivinaQuestGoal obj = subGoals[i];
					obj.OnFail = (Action)Delegate.Combine(obj.OnFail, (Action)delegate
					{
						OnSubGoalFailed(FailReason.MainQuestFail);
					});
				}
				// QuestLog.StartQuest(questID);
			}
			else if (!_delayTracking)
			{
				TrackQuest();
			}
			if (hasSpecificTile)
			{
				if (reUseTile)
				{
					tile = QuestMapGenerator.Instance.FindSpawnedTile(tile.name);
				}
				else
				{
					tile = QuestMapGenerator.Instance.ActivateQuestTile(this);
				}
			}
			if (_hasSubGoals)
			{
				InitializeNextGoal();
			}
			if (hasTimeout)
			{
				StartTimeoutTimer();
			}
		}

		protected void StartTimeoutTimer()
		{
			_questStartTime = ProgressionManager.Instance.StageTime;
			QuestTimeoutObserver.NotifyTimeoutStarted(timeout);
			timeoutCoroutine = StartCoroutine(CheckTimeOutFailure());
		}

		private void TrackQuest()
		{
			if (showQuestTracker)
			{
				// CustomQuestTracker.Instance?.RequestQuestNotification(this, questID, questIcon, createMinimapIcon);
			}
		}

		public virtual void Progress()
		{
			if (!_completed)
			{
				subGoals[_currentGoal].Progress();
			}
		}

		public virtual void Complete()
		{
			if (!_completed)
			{
				if (_isMainQuest)
				{
					RunStatsTracker.Instance?.PlayerStatsEntry.RegisterCompletedQuest();
					// QuestLog.CompleteQuest(questID);
				}
				_completed = true;
				OnComplete?.Invoke();
			}
		}

		protected virtual void OnSubGoalComplete()
		{
			// QuestLog.SetQuestEntryState(questID, _currentGoal, QuestState.Success);
			_currentGoal++;
			if (_currentGoal > subGoals.Length)
			{
				Complete();
			}
			else
			{
				InitializeNextGoal();
			}
		}

		protected void InitializeNextGoal()
		{
			// QuestLog.SetQuestEntryState(questID, _currentGoal, QuestState.Active);
			DivinaQuestGoal divinaQuestGoal = subGoals[_currentGoal - 1];
			divinaQuestGoal.parentQuest = this;
			// divinaQuestGoal.questID = questID;
			divinaQuestGoal.subGoalId = _currentGoal;
			divinaQuestGoal.questIcon = ((divinaQuestGoal.questIcon == null) ? questIcon : divinaQuestGoal.questIcon);
			divinaQuestGoal.OnComplete = (Action)Delegate.Combine(divinaQuestGoal.OnComplete, new Action(OnSubGoalComplete));
			divinaQuestGoal.Init();
		}

		public List<DivinaQuestGoal> GetAllQuestGoals()
		{
			List<DivinaQuestGoal> list = new List<DivinaQuestGoal>();
			list.Add(this);
			for (int i = 0; i < subGoals.Length; i++)
			{
				list.AddRange(subGoals[i].GetAllQuestGoals());
			}
			return list;
		}

		public virtual void FailQuest(FailReason failReason = FailReason.Lost)
		{
			if (questState == QuestState.Failure)
			{
				return;
			}
			if (failReason != FailReason.MainQuestFail)
			{
				if (questFailedIntercom)
				{
					// IntercomManager.Instance.LaunchIntercom(questFailedIntercomConversation, questFailedEntryID, null, IntercomManager.MAX_PRIORITY).Forget();
				}
				questFailedReason = failReason;
			}
			OnQuestFail();
		}

		private void OnSubGoalFailed(FailReason failReason = FailReason.Lost)
		{
			for (int i = 0; i < subGoals.Length; i++)
			{
				subGoals[i].FailQuest(failReason);
			}
		}

		protected virtual void OnQuestFail()
		{
			if (IsMainQuest)
			{
				// QuestLog.SetQuestState(questID, QuestState.Failure);
			}
			else
			{
				// QuestLog.SetQuestEntryState(questID, subGoalId, QuestState.Failure);
			}
			OnFail?.Invoke();
			// if (questTracker != null)
			// {
			// 	CustomQuestTracker.Instance?.RequestQuestNotification(this, questID, questIcon);
			// }
		}

		public bool IsQuestValid()
		{
			if ((bool)eventVerifier)
			{
				return eventVerifier.Verify();
			}
			return true;
		}

		public virtual void StopQuestTimeout()
		{
			if (hasTimeout)
			{
				if (timeoutCoroutine != null)
				{
					StopCoroutine(timeoutCoroutine);
				}
				QuestTimeoutObserver.NotifyTimeoutStopped();
			}
		}

		protected virtual IEnumerator CheckTimeOutFailure()
		{
			while (questState == QuestState.Active)
			{
				yield return new WaitForSeconds(0.5f);
				float num = ProgressionManager.Instance.StageTime - _questStartTime;
				QuestTimeoutObserver.NotifyTimeoutTick(timeout - num);
				if (num > timeout)
				{
					CameraEffects.Instance.PoetDeathScreenFlashEFX();
					FailQuest(FailReason.Timeout);
					break;
				}
			}
		}

		protected Transform RecursiveFindChild(Transform parent, string childName)
		{
			foreach (Transform item in parent)
			{
				if (item.name == childName)
				{
					return item;
				}
				Transform transform2 = RecursiveFindChild(item, childName);
				if (transform2 != null)
				{
					return transform2;
				}
			}
			return null;
		}
	}
}
