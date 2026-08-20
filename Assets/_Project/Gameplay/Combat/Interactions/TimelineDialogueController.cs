using System;
using AstralShift.Control;
using AstralShift.Control.Controllers;
using AstralShift.HellMaiden.Audio;
using AstralShift.HellMaiden.UI;
using AstralShift.Helpers;
using AstralShift.Managers;
using Rewired;

namespace AstralShift.HellMaiden.Interactions
{
	public class TimelineDialogueController : UIController
	{
		public Action OnSkipTimeline;

		private CustomUnityUIPlayerControllerElementGlyph _glyph;

		private bool _canSkip;

		public float skipHoldTime = 1f;

		private bool _requireFreshPress;

		public override void Activate()
		{
			base.Activate();
			MusicPlayer.Instance.SetSnapShot(MusicPlayer.SnapshotID.Normal);
			ControllerLifetime.OnControllerChanged += PointerManager.Instance.SetUIPointer;
			PointerManager.Instance.SetUIPointer();
			PauseManager.Instance.PausePausables();
			GameDirector.Instance.Player.SetInvulnerable(state: true);
			_canSkip = false;
			TimerHoldInteractionTaskHelper.CancelAndDispose();
		}

		public override void Deactivate()
		{
			base.Deactivate();
			ControllerLifetime.OnControllerChanged -= PointerManager.Instance.SetUIPointer;
			PauseManager.Instance.ResumePausables();
			GameDirector.Instance.Player.SetInvulnerable(state: false);
			_canSkip = false;
			TimerHoldInteractionTaskHelper.CancelAndDispose();
		}

		public void OnDestroy()
		{
			ControllerManager.Instance.UnSubscribe(this);
			TimerHoldInteractionTaskHelper.CancelAndDispose();
		}

		public void SetSkip(CustomUnityUIPlayerControllerElementGlyph glyph, bool state, float duration)
		{
			_canSkip = state;
			skipHoldTime = duration;
			_glyph = glyph;
			_glyph?.gameObject.SetActive(_canSkip);
			_glyph?.SetHold(skipHoldTime);
			_requireFreshPress = true;
		}

		private void Update()
		{
			if (_requireFreshPress)
			{
				Rewired.Player player = ReInput.players.GetPlayer(ControllerLifetime.playerId);
				if (player != null && !player.GetButton("UICancel"))
				{
					_requireFreshPress = false;
				}
			}
		}

		public override void UICancelReleased(InputActionEventData data)
		{
			if (_canSkip)
			{
				_requireFreshPress = false;
				TimerHoldInteractionTaskHelper.CancelAndDispose();
			}
		}

		public override void UICancelHeld(InputActionEventData data)
		{
			if (_canSkip && !_requireFreshPress)
			{
				TimerHoldInteractionTaskHelper.ProcessHoldAsync(skipHoldTime, delegate
				{
					_canSkip = false;
					_glyph?.gameObject.SetActive(value: false);
					OnSkipTimeline?.Invoke();
				});
			}
		}
	}
}
