using System;
using AstralShift.Control;
using AstralShift.Control.Controllers;
using AstralShift.HellMaiden.Audio;
using AstralShift.HellMaiden.Data;
// using AstralShift.HellMaiden.Demo;
using AstralShift.HellMaiden.Scenes;
using AstralShift.HellMaiden.UI;
using AstralShift.HellMaiden.UI.Menus;
using AstralShift.Managers;
using Cysharp.Threading.Tasks;
using Rewired;
using UnityEngine;

namespace AstralShift.HellMaiden.Controllers
{
	public class EndScreenController : UIController
	{
		[SerializeField]
		protected UIEndView view;

		public event Action OnAnyInputDown;

		public event Action<float> OnLeftStickVertical;

		public event Action OnCenter2Pressed;

		public event Action OnUISubmitPressed;

		protected void Awake()
		{
			ControllerManager.Instance.Subscribe(this, init: true);
			view.Init();
		}

		public override void Activate()
		{
			base.Activate();
			MusicPlayer.Instance.SetSnapShot(MusicPlayer.SnapshotID.Card);
			PointerManager.Instance.SetUIPointer();
			ControllerLifetime.OnControllerChanged += PointerManager.Instance.SetUIPointer;
			PauseManager.Instance.PauseGame();
			IntercomManager.Instance.StopIntercom(invokeOnEndEvent: false, unscaledTime: true).Forget();
		}

		public override void Deactivate()
		{
			base.Deactivate();
			ControllerLifetime.OnControllerChanged -= PointerManager.Instance.SetUIPointer;
			PauseManager.Instance.ResumeGame();
		}

		public void ReturnToHub()
		{
			if (HasSpecialScreen())
			{
				// ControllerManager.Instance.OverrideGameController<DemoEndScreenController>();
			}
			else
			{
				SceneMaster.Instance.LoadScene(SceneEnum.Hub);
			}
		}

		private bool HasSpecialScreen()
		{
			if (SceneMaster.Instance.CurrentSceneEnum == SceneEnum.BossLevel_Scarmi && !GameDataManager.GetGameTriggerState("DiedIn_ScarmiLibi"))
			{
				return GameDataManager.GetGameInt("Number_ScarmiLibiKillCount") == 1;
			}
			return false;
		}

		public override void AnyInputDown()
		{
			this.OnAnyInputDown?.Invoke();
		}

		public override void AnyMouseInputStateChanged(int button, bool pressed)
		{
			if (pressed)
			{
				this.OnAnyInputDown?.Invoke();
			}
		}

		public override void UILeftStickVertical(InputActionEventData data)
		{
			float axis = data.GetAxis();
			if (axis != 0f)
			{
				this.OnLeftStickVertical?.Invoke(axis);
			}
		}

		public override void UICenter2(InputActionEventData data)
		{
			if (data.eventType == InputActionEventType.ButtonJustPressed)
			{
				this.OnCenter2Pressed?.Invoke();
			}
		}

		public override void UISubmit(InputActionEventData data)
		{
			if (data.eventType == InputActionEventType.ButtonJustPressed)
			{
				this.OnUISubmitPressed?.Invoke();
			}
		}

		private void OnDestroy()
		{
			ControllerManager.Instance.UnSubscribe(this);
		}
	}
}
