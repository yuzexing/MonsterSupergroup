using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Animancer;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Controllers;
using AstralShift.HellMaiden.Data.Perks;
using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.HellMaiden.UI.Perks;
using AstralShift.Helpers;
using AstralShift.Managers;
using AstralShift.UI;
using Cysharp.Threading.Tasks;
using FMOD.Studio;
using FMODUnity;
using PixelCrushers.DialogueSystem;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI.Menus
{
	public class PerkMenuView : MonoBehaviour
	{
		[SerializeField]
		protected PerkMenuController _controller;

		[Header("References")]
		[SerializeField]
		protected Canvas canvas;

		[SerializeField]
		protected GameObject firstSelected;

		[SerializeField]
		protected CanvasGroup perksPanel;

		[SerializeField]
		protected HorizontalCurveLayoutGroup perksLayout;

		[SerializeField]
		protected AnimationCurve defaultPerksLayoutCurve;

		[SerializeField]
		protected AnimationCurve twoPerksLayoutCurve;

		[SerializeField]
		protected AnimationCurve onePerkLayoutCurve;

		[SerializeField]
		protected GraphicRaycaster raycaster;

		[SerializeField]
		protected CanvasGroup perksContainer;

		[SerializeField]
		protected List<Transform> startPositions;

		[SerializeField]
		protected float descriptionPivotHorizontalOffset = 128f;

		[SerializeField]
		protected float descriptionPivotVerticalOffset = 50f;

		[Space]
		[SerializeField]
		protected TextMeshProUGUI reRollAmountText;

		[SerializeField]
		protected UIFadableButton reRollButton;

		[SerializeField]
		protected UIFadableButton banishButton;

		[SerializeField]
		protected TextMeshProUGUI banishesAmountText;

		[Header("Animations")]
		[SerializeField]
		protected ClipTransition openAnimation;

		[SerializeField]
		protected ClipTransition closeAnimation;

		[SerializeField]
		protected ClipTransition rotatingHalo;

		[SerializeField]
		protected ClipTransition wingsLoop;

		[SerializeField]
		protected AnimancerComponent animancer;

		[SerializeField]
		protected AnimancerComponent rarityAnimancer;

		[SerializeField]
		protected ClipTransition normalAnimation;

		[SerializeField]
		protected ClipTransition silverAnimation;

		[SerializeField]
		protected ClipTransition goldAnimation;

		[SerializeField]
		protected ClipTransition LegendaryAnimation;

		[Header("Visuals")]
		[SerializeField]
		protected TMPStringInterpolator plateTextInterpolator;

		[SerializeField]
		protected float plateTextInterpolationDuration;

		[SerializeField]
		protected float plateTextInterpolationDelay;

		[Header("Glyphs")]
		[SerializeField]
		protected CanvasGroup detailsMenuGlyph;

		[SerializeField]
		protected CanvasGroup skipGlyphCanvasGroup;

		[SerializeField]
		protected CustomUnityUIPlayerControllerElementGlyph skipGlyph;

		[SerializeField]
		protected CustomUnityUIPlayerControllerElementGlyph rerollGlyph;

		[SerializeField]
		protected CustomUnityUIPlayerControllerElementGlyph banishGlyph;

		[Header("Sounds")]
		[SerializeField]
		protected EventReference reRollHoldSound;

		[SerializeField]
		protected EventReference bronzeMenuEnterSound;

		[SerializeField]
		protected EventReference silverMenuEnterSound;

		[SerializeField]
		protected EventReference goldMenuEnterSound;

		[SerializeField]
		protected EventReference crystalMenuEnterSound;

		protected string eventName = "event:/sx/dlg/sx_dlg_vo";

		[SerializeField]
		protected List<string> VALineId;

		protected EventInstance _reRollHoldInstance;

		protected bool _isRerollHolding;

		protected GameObject _currentlySelected;

		protected AnimancerState _openCloseAnimationState;

		protected AnimancerState _idleAnimationState;

		protected bool _showingPerksAnimation;

		protected bool _perksCreated;

		protected int _maxReRollsAmount;

		protected int _currentReRollsAmount;

		protected int _maxBanishesAmount;

		protected int _currentBanishesAmount;

		protected CancellationTokenSource _banishCTS;

		protected RuntimePerkData[] _offeringsPerksData;

		protected List<PerkView> _instantiatedPerkViews;

		protected PerkDropTier _currentDropTier;

		protected Action<PerkPoolID, RuntimePerkData> _onPerkSubmitHandler;

		protected Action _onPerkSelectedTweenStartHandler;

		public bool IsInteractable => perksPanel.interactable;

		protected virtual void Awake()
		{
			ControllerManager.Instance.Subscribe(_controller);
			EnableMenuInteraction(state: false);
			EnableMenuVisibility(state: false);
			UnRegisterAllActions();
			_maxReRollsAmount = GameDirector.Instance.Player.PlayerStats.currentStats.perksRerollsAmount;
			_currentReRollsAmount = _maxReRollsAmount;
			RefreshReRollAmountText();
			_maxBanishesAmount = 0;
			_currentBanishesAmount = _maxBanishesAmount;
			RefreshBanishesAmountText();
			_onPerkSubmitHandler = OnPerkSubmit;
			_onPerkSelectedTweenStartHandler = OnPerkSelectedTweenStart;
			raycaster.enabled = false;
		}

		protected virtual void OnDestroy()
		{
			if (_instantiatedPerkViews == null)
			{
				return;
			}
			foreach (PerkView instantiatedPerkView in _instantiatedPerkViews)
			{
				if (instantiatedPerkView != null)
				{
					instantiatedPerkView.onPerkSelectedTweenStart = (Action)Delegate.Remove(instantiatedPerkView.onPerkSelectedTweenStart, _onPerkSelectedTweenStartHandler);
					instantiatedPerkView.OnSubmit = (Action<PerkPoolID, RuntimePerkData>)Delegate.Remove(instantiatedPerkView.OnSubmit, _onPerkSubmitHandler);
				}
			}
		}

		protected virtual void OnPerkSelectedTweenStart()
		{
			EnableMenuInteraction(state: false);
		}

		protected virtual void OnPerkSubmit(PerkPoolID perkPoolID, RuntimePerkData runtimePerkData)
		{
			PlayerHand.Instance.AddPerk(perkPoolID, runtimePerkData);
			Close();
		}

		public virtual async void Open()
		{
			_ = 2;
			try
			{
				if (_instantiatedPerkViews == null)
				{
					_instantiatedPerkViews = new List<PerkView>();
				}
				_instantiatedPerkViews.Clear();
				_offeringsPerksData = Leveler.Instance.PerkPool.GetNewPerksDrop(out _currentDropTier);
				plateTextInterpolator?.ResetQuote();
				ChooseMenuRarity(_currentDropTier);
				if (_offeringsPerksData != null)
				{
					ControllerManager.Instance.OverrideGameController(_controller);
					SetOfferingsLayoutGroupEnable(state: true);
					await UniTask.WhenAll(OpenAnimation(), InstantiatePerksFromData());
					await UniTask.WaitUntil(() => !_showingPerksAnimation);
					plateTextInterpolator?.Interpolate(plateTextInterpolationDuration, plateTextInterpolationDelay);
					await TutorialManager.Instance.CSM.TryLaunchCharmCardsTutorial();
					LayoutRebuilder.ForceRebuildLayoutImmediate(base.transform as RectTransform);
					CreatePerksNavigation();
					_controller.TransitionToWaitingForPick();
				}
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		protected virtual async void Close()
		{
			try
			{
				_controller.TransitionToClose();
				_currentlySelected = null;
				EventSystem.current.SetSelectedGameObject(null);
				UnRegisterAllActions();
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

		public virtual void RegisterAllActions()
		{
			RegisterSkipAction();
			RegisterReRollAction();
			RegisterBanishAction();
		}

		public virtual void UnRegisterAllActions()
		{
			UnRegisterSkipAction();
			UnRegisterReRollAction();
			UnRegisterBanishAction();
		}

		protected virtual void RegisterSkipAction()
		{
			skipGlyphCanvasGroup.alpha = 1f;
			skipGlyph.SetHold(_controller.ConfirmHoldTime);
			_controller.OnUICenter2Hold += TrySkipMenu;
		}

		protected virtual void UnRegisterSkipAction()
		{
			skipGlyphCanvasGroup.alpha = 0f;
			_controller.OnUICenter2Hold -= TrySkipMenu;
		}

		protected void TrySkipMenu(float pressedTime)
		{
			if (!(pressedTime < _controller.ConfirmHoldTime))
			{
				Close();
			}
		}

		protected virtual void RegisterReRollAction()
		{
			if (_currentReRollsAmount == 0)
			{
				UnRegisterReRollAction();
				return;
			}
			rerollGlyph.SetHold(_controller.ConfirmRerollTime);
			reRollButton.Button.onClick.RemoveAllListeners();
			reRollButton.Button.onClick.AddListener(ReRoll);
			_controller.OnUIButton4Pressed += StartReRoll;
			_controller.OnUIButton4Hold += TryReroll;
			_controller.OnUIButton4Released += StopReRoll;
			RefreshReRollAmountText();
			reRollButton.Show();
			reRollButton.Button.interactable = true;
		}

		protected virtual void UnRegisterReRollAction()
		{
			reRollButton.Button.onClick.RemoveAllListeners();
			_controller.OnUIButton4Pressed -= StartReRoll;
			_controller.OnUIButton4Hold -= TryReroll;
			_controller.OnUIButton4Released -= StopReRoll;
			reRollButton.Hide();
			reRollButton.Button.interactable = false;
		}

		private void StartReRoll()
		{
			_isRerollHolding = true;
			StartHoldSound(ref _reRollHoldInstance, reRollHoldSound);
		}

		private void TryReroll(float pressedTime)
		{
			if (_isRerollHolding && !(pressedTime < _controller.ConfirmRerollTime))
			{
				reRollButton.Button.OnSubmit(null);
			}
		}

		private void StopReRoll()
		{
			_isRerollHolding = false;
			StopHoldSound(ref _reRollHoldInstance);
		}

		private async void ReRoll()
		{
			StopReRoll();
			ReleaseHoldSound(ref _reRollHoldInstance);
			try
			{
				if (_currentReRollsAmount != 0)
				{
					_currentReRollsAmount--;
					_controller.TransitionToReRolling();
					await HidePerks();
					DisposeInstantiatedPerks();
					SetOfferingsLayoutGroupEnable(state: true);
					_offeringsPerksData = Leveler.Instance.PerkPool.GetNewPerksDrop(_currentDropTier);
					await InstantiatePerksFromData();
					await ShowPerks();
					CreatePerksNavigation();
					_controller.TransitionToWaitingForPick();
				}
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		protected virtual void RefreshReRollAmountText()
		{
			if (reRollAmountText != null)
			{
				reRollAmountText.text = _currentReRollsAmount.ToString();
			}
		}

		protected virtual void RegisterBanishAction()
		{
			if (banishButton == null)
			{
				return;
			}
			if (_currentBanishesAmount == 0)
			{
				UnRegisterBanishAction();
				return;
			}
			_controller.OnUICancelPressed += TryBanishPerk;
			banishGlyph.SetHold(_controller.BanishPerkHoldTime);
			banishButton.Button.onClick.RemoveAllListeners();
			banishButton.Button.onClick.AddListener(delegate
			{
				StartBanish(_currentlySelected?.GetComponent<PerkView>());
			});
			RefreshBanishesAmountText();
			banishButton.Show();
			banishButton.Button.interactable = true;
		}

		protected virtual void UnRegisterBanishAction()
		{
			if (!(banishButton == null))
			{
				banishButton.Button.onClick.RemoveAllListeners();
				_controller.OnUICancelPressed -= TryBanishPerk;
				banishButton.Hide();
				banishButton.Button.interactable = false;
			}
		}

		private void TryBanishPerk(float pressedTime)
		{
			banishButton.Button.OnSubmit(null);
		}

		public async void StartBanish(PerkView perkView, bool dragMode = false)
		{
			try
			{
				if (!(perkView == null))
				{
					if (_banishCTS == null)
					{
						_banishCTS = new CancellationTokenSource();
					}
					EnableMenuInteraction(state: false);
					await ApplyBanish(perkView);
					EnableMenuInteraction(state: true);
				}
			}
			catch (OperationCanceledException)
			{
				_banishCTS?.Dispose();
				_banishCTS = null;
				EnableMenuInteraction(state: true);
			}
			catch (Exception message)
			{
				Debug.LogError(message);
			}
		}

		public void StopBanish()
		{
			_banishCTS?.Cancel();
		}

		private async UniTask ApplyBanish(PerkView perkToBanish)
		{
			if (perkToBanish == null)
			{
				return;
			}
			int num = _instantiatedPerkViews.IndexOf(perkToBanish);
			if (num >= 0)
			{
				_currentBanishesAmount--;
				RefreshBanishesAmountText();
				_instantiatedPerkViews.RemoveAt(num);
				Leveler.Instance.PerkPool.BanPerk(perkToBanish.PerkData);
				perkToBanish.onPerkSelectedTweenStart = (Action)Delegate.Remove(perkToBanish.onPerkSelectedTweenStart, _onPerkSelectedTweenStartHandler);
				perkToBanish.OnSubmit = (Action<PerkPoolID, RuntimePerkData>)Delegate.Remove(perkToBanish.OnSubmit, _onPerkSubmitHandler);
				UnityEngine.Object.Destroy(perkToBanish.gameObject);
				UpdatePerkPositions();
				if (_instantiatedPerkViews.Count > 0)
				{
					CreatePerksNavigation();
					SetCurrentSelection();
				}
				else
				{
					Close();
				}
				if (_currentBanishesAmount == 0)
				{
					UnRegisterBanishAction();
				}
			}
		}

		protected virtual void RefreshBanishesAmountText()
		{
			banishesAmountText.text = _currentBanishesAmount.ToString();
		}

		public virtual void ShowDetailsMenuGlyph(bool state)
		{
			detailsMenuGlyph.gameObject.SetActive(state);
		}

		public virtual void ShowPerksAnimationEvent()
		{
			ShowPerks().Forget();
		}

		protected virtual async UniTask ShowPerks()
		{
			await UniTask.WaitUntil(() => _perksCreated);
			_showingPerksAnimation = true;
			for (int num = 0; num < _instantiatedPerkViews.Count; num++)
			{
				float y = 0f;
				if (_instantiatedPerkViews.Count == 3)
				{
					switch (num)
					{
					case 0:
						y = -20f;
						break;
					case 1:
						y = 0f;
						break;
					case 2:
						y = 20f;
						break;
					}
				}
				else if (_instantiatedPerkViews.Count == 2)
				{
					y = ((num == 0) ? (-10) : 10);
				}
				_instantiatedPerkViews[num].SetRotationOffset(new Vector3(0f, y, 0f));
			}
			SetOfferingsLayoutGroupEnable(state: false);
			List<UniTask> showTasks = new List<UniTask>();
			for (int num2 = 0; num2 < _instantiatedPerkViews.Count; num2++)
			{
				Transform startPosition = ((num2 < startPositions.Count) ? startPositions[num2] : startPositions[0]);
				showTasks.Add(_instantiatedPerkViews[num2].ShowTween(startPosition));
			}
			await UniTask.NextFrame();
			EnablePerksContainerVisibility(state: true);
			await UniTask.WhenAll(showTasks);
			SetOfferingsLayoutGroupEnable(state: true);
			_instantiatedPerkViews.ForEach(delegate(PerkView perkView)
			{
				perkView.EnableIdleAnimation(state: true);
			});
			_showingPerksAnimation = false;
			_instantiatedPerkViews.ForEach(delegate(PerkView perkView)
			{
				perkView.Perk3DView.CanBeStatic = perkView.PerkData.Rarity != PerkRarity.Crystal;
			});
		}

		protected virtual async UniTask HidePerks()
		{
			SetOfferingsLayoutGroupEnable(state: false);
			List<UniTask> hideTasks = new List<UniTask>();
			for (int i = 0; i < _instantiatedPerkViews.Count; i++)
			{
				Transform endPosition = ((i < startPositions.Count) ? startPositions[i] : startPositions[0]);
				hideTasks.Add(_instantiatedPerkViews[i].HideTween(endPosition));
			}
			await UniTask.NextFrame();
			await UniTask.WhenAll(hideTasks);
		}

		protected virtual void DisposeInstantiatedPerks()
		{
			for (int num = _instantiatedPerkViews.Count - 1; num >= 0; num--)
			{
				PerkView perkView = _instantiatedPerkViews[num];
				if (perkView != null)
				{
					perkView.onPerkSelectedTweenStart = (Action)Delegate.Remove(perkView.onPerkSelectedTweenStart, _onPerkSelectedTweenStartHandler);
					perkView.OnSubmit = (Action<PerkPoolID, RuntimePerkData>)Delegate.Remove(perkView.OnSubmit, _onPerkSubmitHandler);
					UIPerkRenderingManager.Instance.RemovePerk(perkView);
					UnityEngine.Object.Destroy(perkView.gameObject);
				}
			}
			_instantiatedPerkViews.Clear();
		}

		protected virtual async UniTask OpenAnimation()
		{
			raycaster.enabled = true;
			canvas.enabled = true;
			await UniTask.NextFrame();
			_openCloseAnimationState = animancer.Layers[0].Play(openAnimation, openAnimation.FadeDuration);
			while (_openCloseAnimationState.IsPlayingAndNotEnding())
			{
				await UniTask.NextFrame();
			}
			WingsIdle();
			HaloIdle();
		}

		protected virtual async UniTask CloseAnimation()
		{
			await UniTask.NextFrame();
			_openCloseAnimationState = animancer.Layers[0].Play(closeAnimation, closeAnimation.FadeDuration);
			while (_openCloseAnimationState.IsPlayingAndNotEnding())
			{
				await UniTask.NextFrame();
			}
			canvas.enabled = false;
			raycaster.enabled = false;
		}

		private void WingsIdle()
		{
			animancer.Layers[0].Play(wingsLoop, 0f);
		}

		private void HaloIdle()
		{
			animancer.Layers[1].Play(rotatingHalo, 0f);
		}

		private void ChooseMenuRarity(PerkDropTier tier)
		{
			EventReference eventReference = bronzeMenuEnterSound;
			switch (tier)
			{
			case PerkDropTier.Basic:
				eventReference = bronzeMenuEnterSound;
				rarityAnimancer.Play(normalAnimation);
				// DialogueManager.instance.gameObject.GetComponent<FmodProgramerEventPlayer>().PlayRandomDialogueFromList(eventName, VALineId);
				break;
			case PerkDropTier.Silver:
				eventReference = silverMenuEnterSound;
				rarityAnimancer.Play(silverAnimation);
				// DialogueManager.instance.gameObject.GetComponent<FmodProgramerEventPlayer>().PlayRandomDialogueFromList(eventName, VALineId);
				break;
			case PerkDropTier.Gold:
				eventReference = goldMenuEnterSound;
				rarityAnimancer.Play(goldAnimation);
				// DialogueManager.instance.gameObject.GetComponent<FmodProgramerEventPlayer>().PlayRandomDialogueFromList(eventName, VALineId, 1f);
				break;
			case PerkDropTier.Legendary:
				eventReference = crystalMenuEnterSound;
				rarityAnimancer.Play(LegendaryAnimation);
				// DialogueManager.instance.gameObject.GetComponent<FmodProgramerEventPlayer>().PlayRandomDialogueFromList(eventName, VALineId, 1f);
				break;
			}
			if (!eventReference.IsNull)
			{
				RuntimeManager.PlayOneShot(eventReference);
			}
		}

		protected virtual void SetOfferingsLayoutGroupEnable(bool state)
		{
			perksLayout.enabled = state;
		}

		protected virtual void EnableMenuVisibility(bool state)
		{
			perksPanel.alpha = (state ? 1 : 0);
		}

		protected virtual void EnablePerksContainerVisibility(bool state)
		{
			perksContainer.alpha = (state ? 1 : 0);
		}

		public virtual void EnableMenuInteraction(bool state)
		{
			perksPanel.interactable = state;
			perksPanel.blocksRaycasts = state;
		}

		protected virtual async UniTask InstantiatePerksFromData()
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
					perkView.OnSubmit = (Action<PerkPoolID, RuntimePerkData>)Delegate.Combine(perkView.OnSubmit, _onPerkSubmitHandler);
					perkView.gameObject.name = $"Perk {i}";
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

		protected virtual void CreatePerksNavigation()
		{
			PerkView perkView = _instantiatedPerkViews.FirstOrDefault();
			int count = _instantiatedPerkViews.Count;
			if (perkView != null)
			{
				firstSelected = perkView.gameObject;
			}
			for (int i = 0; i < count; i++)
			{
				Navigation navigation = new Navigation
				{
					mode = Navigation.Mode.Explicit
				};
				if (i == 0)
				{
					List<PerkView> instantiatedPerkViews = _instantiatedPerkViews;
					navigation.selectOnLeft = instantiatedPerkViews[instantiatedPerkViews.Count - 1];
				}
				if (i > 0)
				{
					navigation.selectOnLeft = _instantiatedPerkViews[i - 1];
				}
				if (i < count - 1)
				{
					navigation.selectOnRight = _instantiatedPerkViews[i + 1];
				}
				if (i == count - 1)
				{
					navigation.selectOnRight = _instantiatedPerkViews[0];
				}
				PerkView perkView2 = _instantiatedPerkViews[i];
				perkView2.navigation = navigation;
				perkView2.onSelect.RemoveAllListeners();
				perkView2.onPointerEnter.RemoveAllListeners();
				perkView2.onSelect.AddListener(delegate
				{
					_currentlySelected = perkView2.gameObject;
				});
				perkView2.onPointerEnter.AddListener(delegate
				{
					_currentlySelected = perkView2.gameObject;
				});
			}
			_currentlySelected = firstSelected;
		}

		protected virtual void UpdatePerkPositions()
		{
			int count = _instantiatedPerkViews.Count;
			switch (count)
			{
			case 1:
				perksLayout.Curve = onePerkLayoutCurve;
				break;
			case 2:
				perksLayout.Curve = twoPerksLayoutCurve;
				break;
			default:
				perksLayout.Curve = defaultPerksLayoutCurve;
				break;
			}
			if (count == 1)
			{
				_instantiatedPerkViews[0].SetDescriptionOffset(Vector2.zero);
				return;
			}
			float num = ((float)count - 1f) / 2f;
			for (int i = 0; i < _instantiatedPerkViews.Count; i++)
			{
				float num2 = ((float)i - num) / num;
				Vector2 descriptionOffset = new Vector2(descriptionPivotHorizontalOffset * num2, descriptionPivotVerticalOffset * Mathf.Abs(num2));
				_instantiatedPerkViews[i].SetDescriptionOffset(descriptionOffset);
			}
		}

		public virtual void SetCurrentSelection()
		{
			EventSystem.current.SetSelectedGameObject(_currentlySelected);
		}

		protected void StartHoldSound(ref EventInstance instance, EventReference soundRef)
		{
			if (!soundRef.IsNull)
			{
				StopHoldSound(ref instance);
				instance = RuntimeManager.CreateInstance(soundRef);
				instance.start();
			}
		}

		protected void StopHoldSound(ref EventInstance instance)
		{
			if (instance.isValid())
			{
				instance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
				instance.release();
				instance.clearHandle();
			}
		}

		protected void ReleaseHoldSound(ref EventInstance instance)
		{
			if (instance.isValid())
			{
				instance.release();
				instance.clearHandle();
			}
		}
	}
}
