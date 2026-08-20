using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Controllers;
using AstralShift.HellMaiden.Data.Perks;
using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.HellMaiden.UI.Perks;
using AstralShift.Helpers;
using AstralShift.Managers;
using AstralShift.UI;
using Cysharp.Threading.Tasks;
using FMODUnity;
using PixelCrushers.DialogueSystem;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI.Menus
{
	public class BestowalMenuView : PerkMenuView
	{
		private BestowalMenuController _bestowalMenuController;

		[SerializeField]
		private CustomUIButton confirmButton;

		[SerializeField]
		protected EventReference bestowalInSound;

		[SerializeField]
		protected EventReference bestowalOutSound;

		protected override void Awake()
		{
			_bestowalMenuController = _controller as BestowalMenuController;
			ControllerManager.Instance.Subscribe(_bestowalMenuController);
			EnableMenuInteraction(state: false);
			EnableMenuVisibility(state: false);
			UnRegisterAllActions();
			_onPerkSelectedTweenStartHandler = OnPerkSelectedTweenStart;
			raycaster.enabled = false;
			if (_bestowalMenuController != null)
			{
				_bestowalMenuController.OnControllerTypeChanged += HighlightPerks;
			}
		}

		protected override void OnDestroy()
		{
			_bestowalMenuController.OnControllerTypeChanged -= HighlightPerks;
			if (_instantiatedPerkViews == null)
			{
				return;
			}
			foreach (PerkView instantiatedPerkView in _instantiatedPerkViews)
			{
				if (instantiatedPerkView != null)
				{
					instantiatedPerkView.onPerkSelectedTweenStart = (Action)Delegate.Remove(instantiatedPerkView.onPerkSelectedTweenStart, _onPerkSelectedTweenStartHandler);
				}
			}
		}

		protected override async UniTask InstantiatePerksFromData()
		{
			_perksCreated = false;
			EnablePerksContainerVisibility(state: false);
			List<UniTask> allRefresh = new List<UniTask>();
			for (int i = 0; i < _offeringsPerksData.Length; i++)
			{
				RuntimePerkData runtimePerkData = _offeringsPerksData[i];
				if (runtimePerkData != null)
				{
					PerkView perkView = await PerkVisualsFactory.GetUIPerk(runtimePerkData, perksLayout.transform);
					perkView.transform.localRotation = Quaternion.identity;
					perkView.onPerkSelectedTweenStart = (Action)Delegate.Combine(perkView.onPerkSelectedTweenStart, _onPerkSelectedTweenStartHandler);
					perkView.gameObject.name = $"Perk {i}";
					perkView.interactable = false;
					_instantiatedPerkViews.Add(perkView);
					allRefresh.Add(perkView.RefreshLayout());
				}
			}
			await UniTask.WhenAll(allRefresh);
			UpdatePerkPositions();
			await UniTask.NextFrame();
			await UniTask.NextFrame();
			perksLayout.ForceCalculateLayoutInput();
			_perksCreated = true;
		}

		public override async void Open()
		{
			RuntimeManager.PlayOneShot(bestowalInSound);
			try
			{
				if (_instantiatedPerkViews == null)
				{
					_instantiatedPerkViews = new List<PerkView>();
				}
				_instantiatedPerkViews.Clear();
				_offeringsPerksData = Leveler.Instance.PerkPool.GetNewPerksDrop(out _currentDropTier);
				if (_offeringsPerksData != null)
				{
					ControllerManager.Instance.OverrideGameController(_bestowalMenuController);
					// DialogueManager.instance.gameObject.GetComponent<FmodProgramerEventPlayer>().PlayRandomDialogueFromList(eventName, VALineId, 1f);
					SetOfferingsLayoutGroupEnable(state: true);
					await UniTask.WhenAll(OpenAnimation(), InstantiatePerksFromData());
					await UniTask.WaitUntil(() => !_showingPerksAnimation);
					LayoutRebuilder.ForceRebuildLayoutImmediate(base.transform as RectTransform);
					_controller.TransitionToWaitingForPick();
					HighlightPerks();
				}
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		protected override async void Close()
		{
			RuntimeManager.PlayOneShot(bestowalOutSound);
			try
			{
				_controller.TransitionToClose();
				_currentlySelected = null;
				EventSystem.current.SetSelectedGameObject(null);
				UnRegisterAllActions();
				EquipAllPerks();
				await UniTask.Delay(TimeSpan.FromSeconds(0.5), DelayType.UnscaledDeltaTime);
				await CloseAnimation();
				DisposeInstantiatedPerks();
				ControllerManager.Instance.YieldGameController();
				Leveler.Instance.EvalLevelUp();
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		public void HighlightPerks()
		{
			foreach (PerkView instantiatedPerkView in _instantiatedPerkViews)
			{
				instantiatedPerkView.Select();
				instantiatedPerkView.SetSelectedDescription();
			}
		}

		private void PressConfirm()
		{
			confirmButton.onSubmit.Invoke();
		}

		private void EquipAllPerks()
		{
			foreach (PerkView instantiatedPerkView in _instantiatedPerkViews)
			{
				PlayerHand.Instance.AddPerk(PerkPoolID.Beatrice, instantiatedPerkView.PerkData);
				instantiatedPerkView.onSubmit.Invoke();
			}
		}

		public override void UnRegisterAllActions()
		{
			UnRegisterSkipAction();
		}

		public override void RegisterAllActions()
		{
			RegisterSkipAction();
		}

		protected override void RegisterReRollAction()
		{
		}

		protected override void UnRegisterReRollAction()
		{
		}

		protected override void RegisterBanishAction()
		{
		}

		protected override void UnRegisterBanishAction()
		{
		}

		protected override void RegisterSkipAction()
		{
			confirmButton.CanvasGroup.alpha = 1f;
			confirmButton.onSubmit.AddListener(Close);
			_bestowalMenuController.OnUISubmitPressed += PressConfirm;
		}

		protected override void UnRegisterSkipAction()
		{
			confirmButton.CanvasGroup.alpha = 0f;
			confirmButton.onSubmit.RemoveAllListeners();
			_bestowalMenuController.OnUISubmitPressed -= PressConfirm;
		}
	}
}
