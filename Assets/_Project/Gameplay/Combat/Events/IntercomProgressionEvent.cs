using AstralShift.HellMaiden.Dialogue;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.UI;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Events
{
	public class IntercomProgressionEvent : ProgressionEvent
	{
		// public IntercomRuleFlags ruleFlags;

		public float Ttl;

		private float _currentDelayTime;

		private float _startDelayTimestamp;

		// public IntercomConversation conversation { get; set; }

		public float afterBusyDelay { get; set; }

		public override float startTime { get; set; }

		public override float endTime { get; set; }

		public override bool progressionPaused { get; set; }

		public override bool hasEnded { get; set; }

		public bool IsBusy => PlayerState.IsBusy();

		public bool IsLevelingUp => PlayerState.IsLevelingUp();

		public override void Init()
		{
			SetDelayTime(0f);
		}

		public override void ProgressUpdate()
		{
			if (IsBusy || IsLevelingUp)
			{
				SetDelayTime(afterBusyDelay);
			}
			else if (IsDelayTimeElapsed())
			{
				LaunchIntercom();
				hasEnded = true;
			}
		}

		private void LaunchIntercom()
		{
			// IntercomManager.Instance.LaunchIntercom(conversation.Conversation, conversation.EntryID, null, conversation.GetPriority(), ruleFlags, null, Ttl).Forget();
		}

		private void SetDelayTime(float delay)
		{
			_currentDelayTime = delay;
			_startDelayTimestamp = Time.time;
		}

		private bool IsDelayTimeElapsed()
		{
			return Time.time - _startDelayTimestamp >= _currentDelayTime;
		}

		public override void End()
		{
		}
	}
}
