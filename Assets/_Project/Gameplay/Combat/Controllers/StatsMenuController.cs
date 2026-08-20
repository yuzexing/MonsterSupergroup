using System;
using AstralShift.Control;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.UI;
using AstralShift.HellMaiden.UI.Menus;
using AstralShift.Managers;
using Cysharp.Threading.Tasks;
using Rewired;
using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.Controllers
{
	public class StatsMenuController : TabMenuController
	{
		[SerializeField]
		private PlayerStatsInformationPanel _stats;

		[SerializeField]
		private CanvasGroup menuGroup;

		[SerializeField]
		private Button closeBt;

		public Button CloseButton => closeBt;

		public event Action OnDirectionalRight;

		public event Action OnDirectionalLeft;

		public event Action OnDirectionalUp;

		public event Action OnDirectionalDown;

		private void Awake()
		{
			ControllerManager.Instance.Subscribe(this, init: true);
		}

		private void OnDestroy()
		{
			ControllerManager.Instance.UnSubscribe(this);
		}

		public override void Init()
		{
			base.Init();
			menuAnimator.UpdateMode = AnimatorUpdateMode.UnscaledTime;
			menuGroup.alpha = 0f;
			EnableMenuInteraction(state: false);
			RegisterCloseAction();
			if (PlayerHand.Instance.TryGetEquippedSignatureWeapon(out var data))
			{
				_stats.CreateWeapon3D(data.Data).Forget();
			}
		}

		public override void Activate()
		{
			base.Activate();
			ControllerLifetime.OnControllerChanged += PointerManager.Instance.SetPointerForMenuNavigation;
			CombatUIManager.Instance.OpenHUD();
			CombatUIManager.Instance.SelectiveHUD(new CombatUIManager.SelectiveHudRequest
			{
				keepBars = true,
				keepClock = true,
				keepUltimate = true
			});
			PointerManager.Instance.SetUIPointer();
			EnableMenuInteraction(state: false);
		}

		public override void Deactivate()
		{
			base.Deactivate();
			CombatUIManager.Instance.CloseHUD();
			ControllerLifetime.OnControllerChanged -= PointerManager.Instance.SetPointerForMenuNavigation;
		}

		public async UniTaskVoid AsyncDeactivateHUD()
		{
			await CloseHUDAsync();
			Debug.Log("base.Deactivate");
			static async UniTask CloseHUDAsync()
			{
				CombatUIManager.Instance.SelectiveHUD(new CombatUIManager.SelectiveHudRequest
				{
					keepBars = false,
					keepClock = false,
					keepUltimate = false
				});
				await UniTask.Yield();
			}
		}

		public void EnableMenuInteraction(bool state)
		{
			menuGroup.interactable = state;
			menuGroup.blocksRaycasts = state;
		}

		private void RegisterCloseAction()
		{
			closeBt.onClick.RemoveAllListeners();
			closeBt.onClick.AddListener(CloseMenu);
		}

		protected override void OnControllerTypeChanged()
		{
			if (_currentMenu != null)
			{
				currentSelectable = _currentMenu.currentSelected;
				base.OnControllerTypeChanged();
			}
		}

		public override void Open()
		{
			_stats.Show();
			base.Open();
		}

		protected override void OnOpeningFinished()
		{
			base.OnOpeningFinished();
			EnableMenuInteraction(state: true);
			RefreshGlyphsAsync().Forget();
		}

		private async UniTaskVoid RefreshGlyphsAsync()
		{
			await UniTask.DelayFrame(1, PlayerLoopTiming.Update, this.GetCancellationTokenOnDestroy());
			if (_currentMenu != null)
			{
				currentSelectable = _currentMenu.currentSelected;
				base.OnControllerTypeChanged();
			}
		}

		protected override void CloseMenu()
		{
			EnableMenuInteraction(state: false);
			AsyncDeactivateHUD().Forget();
			Close();
		}

		public void CleanControllerActions()
		{
			this.OnDirectionalDown = null;
			this.OnDirectionalUp = null;
			this.OnDirectionalRight = null;
			this.OnDirectionalLeft = null;
		}

		public override void UICenter1(InputActionEventData data)
		{
			if (base.IsActive && data.eventType == InputActionEventType.ButtonJustPressed)
			{
				closeBt.OnSubmit(null);
			}
		}

		public override void UICancelPressed(InputActionEventData data)
		{
			if (base.IsActive && data.eventType == InputActionEventType.ButtonJustPressed)
			{
				closeBt.OnSubmit(null);
			}
		}

		public override void UIDirectionalRight(InputActionEventData data)
		{
			if (base.IsActive && data.eventType == InputActionEventType.ButtonJustPressed)
			{
				this.OnDirectionalRight?.Invoke();
			}
		}

		public override void UIDirectionalLeft(InputActionEventData data)
		{
			if (base.IsActive && data.eventType == InputActionEventType.NegativeButtonJustPressed)
			{
				this.OnDirectionalLeft?.Invoke();
			}
		}

		public override void UIDirectionalUp(InputActionEventData data)
		{
			if (base.IsActive && data.eventType == InputActionEventType.ButtonJustPressed)
			{
				this.OnDirectionalUp?.Invoke();
			}
		}

		public override void UIDirectionalDown(InputActionEventData data)
		{
			if (base.IsActive && data.eventType == InputActionEventType.NegativeButtonJustPressed)
			{
				this.OnDirectionalDown?.Invoke();
			}
		}
	}
}
