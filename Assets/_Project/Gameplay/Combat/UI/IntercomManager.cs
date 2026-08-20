using System;
using System.Collections.Generic;
using System.Threading;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Dialogue;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Scenes;
using AstralShift.HellMaiden.UI.Barks;
using AstralShift.Helpers.DialogueHelpers;
using Cysharp.Threading.Tasks;
using PixelCrushers.DialogueSystem;
using UnityEngine;

namespace AstralShift.HellMaiden.UI
{
	public class IntercomManager : MonoBehaviour
	{
		public class IntercomRequest
		{
			public string Conversation;

			public int EntryId;

			public Action OnEnd;

			public UniTaskCompletionSource TaskSource;

			public int Priority;

			public float SystemTimestamp;

			public float Timestamp;

			public float Ttl = 10f;

			public IntercomRuleFlags RuleSet;

			public Func<bool> CustomRuleSet;
		}

		public static IntercomManager Instance;

		// [SerializeField]
		// private IntercomBark intercomBark;

		private string _currentConversationString;

		// private Conversation _currentConversation;

		private int _currentEntry;

		private bool _startingConversation;

		// private AstralDialogueActor _previousDialogueActor;

		// private List<AstralDialogueActor> _usedDialogueActors = new List<AstralDialogueActor>();

		private Action _onIntercomEnd;

		private CancellationTokenSource _intercomCts;

		private readonly List<IntercomRequest> _intercomQueue = new List<IntercomRequest>();

		private bool _isProcessingQueue;

		public static int MAX_PRIORITY = 100;

		public bool IsIntercomActive { get; private set; }

		public IReadOnlyList<IntercomRequest> IntercomQueue => _intercomQueue;

		public IntercomRequest CurrentIntercomRequest { get; private set; }

		public IntercomRequest PendingIntercomRequest { get; private set; }

		private float QueueDelay => 1f;

		public void Init()
		{
			Instance = this;
			SceneMaster.Instance.OnSceneHideFinishPersist += delegate
			{
				ClearQueue();
				StopIntercom(invokeOnEndEvent: false, unscaledTime: true).Forget();
			};
		}

		private void OnDestroy()
		{
			_intercomCts?.Cancel();
			_intercomCts?.Dispose();
			_intercomCts = null;
			while (_intercomQueue.Count > 0)
			{
				_intercomQueue[0].TaskSource.TrySetCanceled();
				_intercomQueue.RemoveAt(0);
			}
			Instance = null;
		}

		public async UniTask LaunchIntercom(string conversation, int entryId = 0, Action onEnd = null, int priority = 0, IntercomRuleFlags ruleSet = IntercomRuleFlags.BlockIfBusy | IntercomRuleFlags.BlockIfLeveling, Func<bool> customRule = null, float Ttl = -1f)
		{
			Debug.Log("IntercomManager: Launching intercom " + conversation);
			CancellationToken destroyToken = this.GetCancellationTokenOnDestroy();
			if (priority == -1)
			{
				ClearQueue();
				if (IsIntercomActive)
				{
					await StopIntercom();
				}
			}
			UniTaskCompletionSource uniTaskCompletionSource = new UniTaskCompletionSource();
			_intercomQueue.Add(new IntercomRequest
			{
				Conversation = conversation,
				EntryId = entryId,
				OnEnd = onEnd,
				TaskSource = uniTaskCompletionSource,
				Priority = priority,
				SystemTimestamp = DateTime.UtcNow.Ticks,
				Timestamp = Time.time,
				Ttl = Ttl,
				RuleSet = ruleSet,
				CustomRuleSet = customRule
			});
			_intercomQueue.Sort(delegate(IntercomRequest a, IntercomRequest b)
			{
				int num = b.Priority.CompareTo(a.Priority);
				if (num == 0)
				{
					num = a.SystemTimestamp.CompareTo(b.SystemTimestamp);
				}
				return num;
			});
			if (!_isProcessingQueue)
			{
				ProcessQueue().Forget();
			}
			await uniTaskCompletionSource.Task.AttachExternalCancellation(destroyToken);
		}

		private async UniTaskVoid ProcessQueue()
		{
			Debug.Log("IntercomManager: Process Queue ");
			_isProcessingQueue = true;
			CancellationToken destroyToken = this.GetCancellationTokenOnDestroy();
			try
			{
				while (_intercomQueue.Count > 0)
				{
					IntercomRequest request = _intercomQueue[0];
					if (request.Ttl > 0f && request.Timestamp + request.Ttl < Time.time)
					{
						_intercomQueue.RemoveAt(0);
						continue;
					}
					if (!EvaluateRules(request))
					{
						PendingIntercomRequest = request;
						await UniTask.Delay(TimeSpan.FromSeconds(QueueDelay), ignoreTimeScale: false, PlayerLoopTiming.Update, destroyToken);
						continue;
					}
					PendingIntercomRequest = null;
					CurrentIntercomRequest = _intercomQueue[0];
					_intercomQueue.RemoveAt(0);
					_onIntercomEnd = request.OnEnd;
					IsIntercomActive = true;
					_currentConversationString = request.Conversation;
					_currentEntry = request.EntryId;
					_intercomCts = CancellationTokenSource.CreateLinkedTokenSource(destroyToken);
					try
					{
						await RunIntercomLoop(_intercomCts.Token);
					}
					catch (OperationCanceledException)
					{
						request.TaskSource.TrySetCanceled();
						break;
					}
					finally
					{
						_intercomCts?.Dispose();
						_intercomCts = null;
					}
					request.TaskSource.TrySetResult();
					if (_intercomQueue.Count > 0)
					{
						await UniTask.Delay(TimeSpan.FromSeconds(QueueDelay), ignoreTimeScale: false, PlayerLoopTiming.Update, destroyToken);
					}
				}
			}
			finally
			{
				_isProcessingQueue = false;
			}
		}

		private bool EvaluateRules(IntercomRequest request)
		{
			if (request.RuleSet.HasFlag(IntercomRuleFlags.BlockInQuest) && PlayerState.IsInQuest())
			{
				return false;
			}
			if (request.RuleSet.HasFlag(IntercomRuleFlags.BlockIfBusy) && PlayerState.IsBusy())
			{
				return false;
			}
			if (request.RuleSet.HasFlag(IntercomRuleFlags.BlockIfLeveling) && PlayerState.IsLevelingUp())
			{
				return false;
			}
			if (request.CustomRuleSet != null)
			{
				return request.CustomRuleSet();
			}
			return true;
		}

		private void ClearQueue()
		{
			foreach (IntercomRequest item in _intercomQueue)
			{
				item.OnEnd?.Invoke();
				item.TaskSource.TrySetCanceled();
			}
			_intercomQueue.Clear();
			CurrentIntercomRequest = null;
			PendingIntercomRequest = null;
		}

		public async UniTask StopIntercom(bool invokeOnEndEvent = true, bool unscaledTime = false)
		{
			if (IsIntercomActive)
			{
				IsIntercomActive = false;
				CurrentIntercomRequest = null;
				PendingIntercomRequest = null;
				_intercomCts?.Cancel();
				// if (intercomBark != null)
				// {
				// 	intercomBark.StopTypewriter();
				// 	await intercomBark.CloseAnimation(unscaledTime);
				// }
				ResetIntercom();
				if (invokeOnEndEvent)
				{
					_onIntercomEnd?.Invoke();
				}
				_onIntercomEnd = null;
			}
		}

		private void ResetUsedActors()
		{
			// foreach (AstralDialogueActor usedDialogueActor in _usedDialogueActors)
			// {
			// 	if (usedDialogueActor != null)
			// 	{
			// 		usedDialogueActor.SetIntercomPortraitExpression(PuppetAnimator.PuppetExpression.Default);
			// 	}
			// }
			// _usedDialogueActors.Clear();
		}

		private void ResetIntercom()
		{
			ResetUsedActors();
			// _previousDialogueActor = null;
			// _currentConversation = null;
			_currentEntry = 0;
			_startingConversation = false;
		}

		private async UniTask RunIntercomLoop(CancellationToken token)
		{
			// Debug.Log("IntercomManager:  Intercom Loop");
			// try
			// {
			// 	_ = 2;
			// 	try
			// 	{
			// 		_currentConversation = DialogueManager.masterDatabase.GetConversation(_currentConversationString);
			// 		_startingConversation = true;
			// 		AstralDialogueManager.Instance.SetDialogueMode(AstralDialogueManager.DialogueMode.Intercom);
			// 		SetFirstDialogueEntry();
			// 		await intercomBark.OpenAnimation().AttachExternalCancellation(token);
			// 		intercomBark.ResumeIntercom();
			// 		while (IsIntercomActive && !token.IsCancellationRequested)
			// 		{
			// 			Debug.Log("IntercomManager: Waiting for line to complete ");
			// 			await intercomBark.WaitForCompletion().AttachExternalCancellation(token);
			// 			Debug.Log("IntercomManager: Line Completed ");
			// 			if (_currentConversation.GetDialogueEntry(_currentEntry).outgoingLinks.Count != 0)
			// 			{
			// 				Debug.Log("IntercomManager: Waiting for next line entry");
			// 				await SetNextDialogueEntry(token);
			// 				intercomBark.ResumeIntercom();
			// 				continue;
			// 			}
			// 			break;
			// 		}
			// 	}
			// 	catch (OperationCanceledException)
			// 	{
			// 	}
			// 	catch (Exception exception)
			// 	{
			// 		Debug.LogException(exception);
			// 	}
			// }
			// finally
			// {
			// 	GameDataManager.RegisterDialogue(_currentConversationString);
			// 	if (!this.GetCancellationTokenOnDestroy().IsCancellationRequested)
			// 	{
			// 		await StopIntercom();
			// 	}
			// 	else
			// 	{
			// 		IsIntercomActive = false;
			// 		CurrentIntercomRequest = null;
			// 		PendingIntercomRequest = null;
			// 		ResetIntercom();
			// 	}
			// }
		}

		private void SetFirstDialogueEntry()
		{
			// _startingConversation = false;
			// _currentConversation = DialogueManager.masterDatabase.GetConversation(_currentConversationString);
			// if (_currentEntry == 0)
			// {
			// 	_currentEntry = _currentConversation.GetFirstDialogueEntry().outgoingLinks[0].destinationDialogueID;
			// }
			// Subtitle barkSubtitle = DialogueHelpers.GetBarkSubtitle(_currentConversationString, _currentEntry, null, null);
			// _previousDialogueActor = barkSubtitle.speakerInfo.transform.GetComponent<AstralDialogueActor>();
			// SetupBarkInternal(barkSubtitle);
		}

		private async UniTask SetNextDialogueEntry(CancellationToken token)
		{
			// DialogueEntry dialogueEntry = _currentConversation.GetDialogueEntry(_currentEntry);
			// _currentEntry = dialogueEntry.outgoingLinks[0].destinationDialogueID;
			// Subtitle nextSubtitle = DialogueHelpers.GetBarkSubtitle(_currentConversationString, _currentEntry, null, null);
			// AstralDialogueActor component = nextSubtitle.speakerInfo.transform.GetComponent<AstralDialogueActor>();
			// if (IsActorChanged(component))
			// {
			// 	_previousDialogueActor = component;
			// 	await intercomBark.FadeOutAnimation().AttachExternalCancellation(token);
			// 	SetupBarkInternal(nextSubtitle);
			// 	await intercomBark.FadeInAnimation().AttachExternalCancellation(token);
			// }
			// else
			// {
			// 	SetupBarkInternal(nextSubtitle);
			// }
		}

		// private void SetupBarkInternal(Subtitle subtitle = null)
		// {
		// 	// Subtitle subtitle2 = subtitle ?? DialogueHelpers.GetBarkSubtitle(_currentConversationString, _currentEntry, null, null);
		// 	// DialogueManager.instance.StartCoroutine(BarkController.Bark(subtitle2, subtitle2.speakerInfo.transform, subtitle2.listenerInfo.transform, intercomBark));
		// }

		// private bool IsActorChanged(AstralDialogueActor actor)
		// {
		// 	// if ((bool)_previousDialogueActor)
		// 	// {
		// 	// 	return _previousDialogueActor.GetInstanceID() != actor.GetInstanceID();
		// 	// }
		// 	// return true;
		// }

		// public void RegisterUsedActor(AstralDialogueActor actor)
		// {
		// 	// if (!_usedDialogueActors.Contains(actor))
		// 	// {
		// 	// 	_usedDialogueActors.Add(actor);
		// 	// }
		// }
	}
}
