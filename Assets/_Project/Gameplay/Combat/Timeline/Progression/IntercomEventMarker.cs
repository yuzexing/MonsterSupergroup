using System.ComponentModel;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Events;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Dialogue;
using UnityEngine;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline.Progression
{
	[CustomStyle("IntercomEventMarkerStyle")]
	[DisplayName("AstralShift/Progression/Intercom Event")]
	public class IntercomEventMarker : ProgressionEventMarker
	{
		[SerializeField]
		[Tooltip("Used only for disambiguation.")]
		private string eventName;

		// [SerializeField]
		// private IntercomConversation conversation;

		[SerializeField]
		private bool replayable;

		[SerializeField]
		private IntercomRuleFlags ruleFlags;

		public DialogueLUTTriggerDependency[] triggerDependencies;

		public DialogueLUTNumberDependency[] numberDependencies;

		[SerializeField]
		private float afterBusyStateDelay = 5f;

		[SerializeField]
		[Tooltip("Time to wait in queue.")]
		private float ttl = 5f;

		// public IntercomConversation Conversation => conversation;

		public IntercomEventMarker(PropertyName id)
			: base(id)
		{
		}

		public override void ProcessEvent(ProgressionTimeline timeline)
		{
			// if ((replayable || !GameDataManager.HasDialoguePlayed(conversation.Conversation)) && ValidateDependencies())
			// {
			// 	IntercomProgressionEvent intercomProgressionEvent = new IntercomProgressionEvent();
			// 	// intercomProgressionEvent.conversation = conversation;
			// 	// intercomProgressionEvent.ruleFlags = ruleFlags;
			// 	intercomProgressionEvent.afterBusyDelay = afterBusyStateDelay;
			// 	intercomProgressionEvent.Ttl = ttl;
			// 	timeline.CreateMilestone(intercomProgressionEvent, this);
			// }
		}

		private bool ValidateDependencies()
		{
			if (triggerDependencies != null)
			{
				for (int i = 0; i < triggerDependencies.Length; i++)
				{
					DialogueLUTTriggerDependency dialogueLUTTriggerDependency = triggerDependencies[i];
					// if (GameDataManager.GetGameTriggerState(dialogueLUTTriggerDependency.variable) != dialogueLUTTriggerDependency.state)
					// {
					// 	return false;
					// }
				}
			}
			if (numberDependencies != null)
			{
				for (int j = 0; j < numberDependencies.Length; j++)
				{
					DialogueLUTNumberDependency dialogueLUTNumberDependency = numberDependencies[j];
					// if (!dialogueLUTNumberDependency.Compare(GameDataManager.GetGameInt(dialogueLUTNumberDependency.variable)))
					// {
					// 	return false;
					// }
				}
			}
			return true;
		}
	}
}
