using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Animancer;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Controllers;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.GameStats;
using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.HellMaiden.UI.Cards;
using AstralShift.HellMaiden.UI.Menus.Hand;
using AstralShift.Helpers;
using AstralShift.Helpers.Collections;
using AstralShift.Managers;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using FMOD.Studio;
using FMODUnity;
using PixelCrushers.DialogueSystem;
using Sirenix.Utilities;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI.Menus
{
	public class UICardPickMenuView : MonoBehaviour
	{
		[Serializable]
		private class PoetVisualsGroup
		{
			public CanvasGroup Background;

			public CanvasGroup Foreground;

			public CanvasGroup Banner;

			public TMPStringInterpolator PlateWeaponText;

			public TMPStringInterpolator PlateModText;

			public Material FrameGemsMaterial;

			[Header("Voice Acting Keys")]
			public List<string> VALineIds = new List<string>();
		}

		public static UICardPickMenuView Instance;

		[SerializeField]
		private CardPickMenuController _controller;

		[Space]
		[SerializeField]
		protected PlayerHandView handView;

		[Space]
		[SerializeField]
		protected CanvasGroup parentGroup;

		[SerializeField]
		protected HorizontalCurveLayoutGroup offeringsLayoutGroup;

		private float _defaultAngleMultiplier;

		private float _defaultMaxOffset;

		[SerializeField]
		private CanvasGroup offeringsCanvasGroup;

		[SerializeField]
		protected UICardViewContainer offeringsCardViewContainer;

		[SerializeField]
		protected Canvas offeringsCardViewContainerCanvas;

		[SerializeField]
		protected Transform onDragContainer;

		[SerializeField]
		protected UICardViewContainer selectedCardViewContainer;

		[SerializeField]
		protected UICardViewContainer backVisualsContainer;

		[SerializeField]
		protected UICardViewContainer frontVisualsContainer;

		[Space]
		[SerializeField]
		private UIFocusParentSwitcherElement selectedFocusElement;

		[SerializeField]
		private UIFocusParentSwitcherElement offeringFocus;

		[SerializeField]
		private UIFocusParentSwitcherElement handFocus;

		[Space]
		[SerializeField]
		private UICardMouseDropButton reRollButton;

		[SerializeField]
		private TextMeshProUGUI reRollAmountText;

		[SerializeField]
		private RectTransform reRollCardPivot;

		[SerializeField]
		private UICardMouseDropButton discardButton;

		[SerializeField]
		private RectTransform discardCardPivot;

		[SerializeField]
		private UICardMouseDropButton banishButton;

		[SerializeField]
		private RectTransform banishCardPivot;

		[SerializeField]
		private TextMeshProUGUI banishesAmountText;

		[Space]
		[SerializeField]
		protected AnimancerComponent animancer;

		[SerializeField]
		protected ClipTransition openWeaponAnimation;

		[SerializeField]
		protected ClipTransition closeWeaponAnimation;

		[SerializeField]
		protected ClipTransition openEquipmentAnimation;

		[SerializeField]
		protected ClipTransition closeEquipmentAnimation;

		[SerializeField]
		protected CPMAnimationSettings animationSettings;

		[Space]
		[SerializeField]
		private CanvasGroup weaponThorns;

		[SerializeField]
		private CanvasGroup weaponFrame;

		[SerializeField]
		private Image[] weaponFrameGems;

		[SerializeField]
		private CanvasGroup equipmentThorns;

		[SerializeField]
		private CanvasGroup equipmentFrame;

		[SerializeField]
		private float plateTextInterpolationDelay = 1f;

		[SerializeField]
		private float plateTextInterpolationDuration = 4f;

		[SerializeField]
		private PoetVisualsGroup danteVisuals;

		[SerializeField]
		private PoetVisualsGroup virgilVisuals;

		[SerializeField]
		private PoetVisualsGroup horaceVisuals;

		[SerializeField]
		private PoetVisualsGroup homerVisuals;

		[SerializeField]
		private PoetVisualsGroup ovidVisuals;

		[SerializeField]
		private PoetVisualsGroup lucanVisuals;

		private Dictionary<PoetPoolID, PoetVisualsGroup> _visualsGroups = new Dictionary<PoetPoolID, PoetVisualsGroup>();

		private TMPStringInterpolator _currentPlateInterpolator;

		private RuntimeCardData[] _offeringsCardsData;

		private List<UICardViewHandler> _offeringsCards;

		[Space]
		[SerializeField]
		private EventReference cardOfferingsEnterSound;

		[SerializeField]
		private EventReference cardHandEnterSound;

		[SerializeField]
		private EventReference banishHoldSound;

		[SerializeField]
		private EventReference reRollHoldSound;

		[SerializeField]
		private EventReference discardHoldSound;

		private EventInstance _banishHoldInstance;

		private EventInstance _reRollHoldInstance;

		private EventInstance _discardHoldInstance;

		[Space]
		[SerializeField]
		private CanvasGroup skipGlyphContainer;

		[SerializeField]
		private CustomUnityUIPlayerControllerElementGlyph skipGlyph;

		[SerializeField]
		private CustomUnityUIPlayerControllerElementGlyph rerollGlyph;

		[SerializeField]
		private CustomUnityUIPlayerControllerElementGlyph discardButtonGlyph;

		[SerializeField]
		private CustomUnityUIPlayerControllerElementGlyph banishButtonGlyph;

		[SerializeField]
		private CanvasGroup detailsMenuGlyph;

		[Space]
		[SerializeField]
		private CanvasGroup detailsAndContextualContainer;

		[SerializeField]
		private RectTransform contextualGlyphsContainer;

		[SerializeField]
		private GameObject dropCardAcceptGlyph;

		[SerializeField]
		private GameObject equipCardAcceptGlyph;

		[SerializeField]
		private GameObject equipCardCancelGlyph;

		[SerializeField]
		private GameObject swapCardAcceptGlyph;

		[SerializeField]
		private GameObject swapCardCancelGlyph;

		[SerializeField]
		private GameObject swapHandSlotModeAcceptGlyph;

		[SerializeField]
		private GameObject swapHandSlotModeCancelGlyph;

		[SerializeField]
		private GameObject swapHandSlotAcceptGlyph;

		private bool _isWeaponDropLayout;

		private int _maxReRollsAmount;

		private int _currentReRollsAmount;

		private int _maxBanishesAmount;

		private int _currentBanishesAmount;

		private bool _canBanish = true;

		private LayerMask _cullingMask;

		private AnimancerState _openCloseAnimationState;

		private Sequence _cardSpawnSequence;

		private CancellationTokenSource _reRollCTS;

		private const float ReRollStopSpeedMultiplier = 2f;

		private const int DiscardReturnToControlTimeoutInMs = 1000;

		private const float DiscardStopSpeedMultiplier = 2f;

		private CancellationTokenSource _discardCTS;

		private const int BanishReturnToControlTimeoutInMs = 1000;

		private const float BanishStopSpeedMultiplier = 2f;

		private CancellationTokenSource _banishCTS;

		public CardPickMenuController Controller => _controller;

		public PlayerHandView HandView => handView;

		public bool IsInteractable => parentGroup.interactable;

		public UICardViewContainer SelectedCardViewContainer => selectedCardViewContainer;

		public UICardViewContainer BackVisualsContainer => backVisualsContainer;

		public UICardViewContainer FrontVisualsContainer => frontVisualsContainer;

		public UIFocusParentSwitcherElement SelectedFocusElement => selectedFocusElement;

		public UIFocusParentSwitcherElement OfferingFocus => offeringFocus;

		public UIFocusParentSwitcherElement HandFocus => handFocus;

		public List<UICardViewHandler> CardsInOfferings => _offeringsCards;

		public bool IsWeaponDropLayout => _isWeaponDropLayout;

		public async UniTask Init()
		{
			if (!Instance)
			{
				Instance = this;
			}
			ControllerManager.Instance.Subscribe(_controller);
			await HandView.Init();
			parentGroup.alpha = 0f;
			_defaultAngleMultiplier = offeringsLayoutGroup.AngleMultiplier;
			_defaultMaxOffset = offeringsLayoutGroup.MaxOffset;
			animancer.UpdateMode = AnimatorUpdateMode.UnscaledTime;
			EnableMenuInteraction(state: false);
			_maxReRollsAmount = GameDirector.Instance.Player.PlayerStats.currentStats.cardsReRollsAmount;
			_currentReRollsAmount = _maxReRollsAmount;
			RefreshReRollAmountText();
			_maxBanishesAmount = GameDirector.Instance.Player.PlayerStats.currentStats.cardBanishesAmount;
			_currentBanishesAmount = _maxBanishesAmount;
			_canBanish = true;
			HandView.OnBeforeUnFold += SetOfferingsGroupUnFocusedMinimizedLayout;
			HandView.OnFold += SetOfferingsGroupUnFocusedDefaultLayout;
			UnRegisterSkipBindings();
			RefreshBanishesAmountText();
			RegisterBanishButton();
			RegisterDiscardButton();
			EnableDiscard(state: false);
			EnableBanish(state: false);
			EnableReRoll(state: false);
			EnableContextualGlyphs(state: false);
			InitVisualGroups();
		}

		private void InitVisualGroups()
		{
			if (_visualsGroups == null)
			{
				_visualsGroups = new Dictionary<PoetPoolID, PoetVisualsGroup>();
			}
			_visualsGroups.TryAdd(PoetPoolID.Dante, danteVisuals);
			_visualsGroups.TryAdd(PoetPoolID.Virgil, virgilVisuals);
			_visualsGroups.TryAdd(PoetPoolID.Horace, horaceVisuals);
			_visualsGroups.TryAdd(PoetPoolID.Homer, homerVisuals);
			_visualsGroups.TryAdd(PoetPoolID.Ovid, ovidVisuals);
			_visualsGroups.TryAdd(PoetPoolID.Lucan, lucanVisuals);
		}

		public async void Open()
		{
			_ = 5;
			try
			{
				if (GetNewCardsData(out var isWeaponDrop, out var poolID))
				{
					OverrideGameController();
					if (isWeaponDrop)
					{
						SetViewWeaponDropLayout();
						PlayPoetVA(poolID);
					}
					else
					{
						SetViewDefaultDropLayout();
					}
					SetBackgroundVisuals(poolID);
					await UniTask.NextFrame();
					await Task.WhenAll(InstantiateCardViews(), OpenAnimation());
					await CardsSpawnAnimation();
					await UniTask.NextFrame();
					_currentPlateInterpolator?.Interpolate(plateTextInterpolationDuration, plateTextInterpolationDelay);
					await UniTask.NextFrame();
					await TryShowCardsTutorial();
					_canBanish = true;
					EnableOfferingCardsInteraction(state: true);
					_controller.TransitionToWaitingPick();
				}
			}
			catch (Exception message)
			{
				Debug.LogError(message);
			}
		}

		public async void Close(int timeoutInMS = 0)
		{
			_ = 1;
			try
			{
				_controller.TransitionToClose();
				await UniTask.Delay(timeoutInMS, ignoreTimeScale: true);
				UICardRenderingManager.Instance.DisposeUnusedStaticTextures();
				await Task.WhenAll(CloseAnimation(), CardsDespawnAnimation().AsTask());
				HandView.FoldHand(instant: true);
				DisposeOfferings();
				YieldGameController();
				UnSelectEventSystemObject();
				Leveler.Instance.EvalLevelUp();
			}
			catch (Exception message)
			{
				Debug.LogError(message);
			}
		}

		private void OnDestroy()
		{
			Instance = null;
		}

		private void OverrideGameController()
		{
			_controller = ControllerManager.Instance.OverrideGameController<CardPickMenuController>();
		}

		private void YieldGameController()
		{
			ControllerManager.Instance.YieldGameController();
		}

		private bool GetNewCardsData(out bool isWeaponDrop, out PoetPoolID poolID)
		{
			_offeringsCardsData = Leveler.Instance.CardPool.GetCardsDrop(Leveler.Instance.Level, out isWeaponDrop, out poolID);
			return _offeringsCardsData != null;
		}

		private bool GetReRollCardsData(out bool isWeaponDrop)
		{
			_offeringsCardsData = Leveler.Instance.CardPool.ReRollCardsDrop(Leveler.Instance.Level, out isWeaponDrop);
			return _offeringsCardsData != null;
		}

		private async Task InstantiateCardViews()
		{
			offeringsLayoutGroup.ForceCalculateLayoutInput();
			if (_offeringsCards == null)
			{
				_offeringsCards = new List<UICardViewHandler>();
			}
			else
			{
				DisposeOfferings();
			}
			List<Task<UICardViewHandler>> allInstantiationTasks = new List<Task<UICardViewHandler>>();
			for (int i = 0; i < _offeringsCardsData.Length; i++)
			{
				RuntimeCardData runtimeCardData = _offeringsCardsData[i];
				if (runtimeCardData != null)
				{
					allInstantiationTasks.Add(InstantiateCardView(runtimeCardData));
				}
			}
			await Task.WhenAll(allInstantiationTasks);
			allInstantiationTasks.Shuffle();
			for (int j = 0; j < allInstantiationTasks.Count; j++)
			{
				AddCardViewToOfferingsGroup(allInstantiationTasks[j].Result, j);
			}
			await UniTask.NextFrame();
			offeringsLayoutGroup.ForceCalculateLayoutInput();
		}

		private async Task<UICardViewHandler> InstantiateCardView(RuntimeCardData data)
		{
			UICardViewHandler obj = await CardVisualsFactory.GetUICard(data, offeringsLayoutGroup.transform);
			obj.gameObject.AddComponent<CPMCardViewInputHandler>();
			obj.Hide();
			obj.AllowInteraction(value: false);
			obj.CardView.DisableTilt();
			obj.CardView.DisableMovement();
			return obj;
		}

		private void DisposeOfferings()
		{
			for (int i = 0; i < _offeringsCards.Count; i++)
			{
				UnityEngine.Object.Destroy(_offeringsCards[i].gameObject);
			}
			_offeringsCards.Clear();
		}

		public async void PickCard(UICardViewHandler cardViewHandler)
		{
			try
			{
				if ((bool)cardViewHandler)
				{
					offeringsLayoutGroup.ForceCalculateLayoutInput();
					Leveler.Instance.CardPool.RegisterChosenCard(cardViewHandler.RuntimeCardData);
					_offeringsCards.Remove(cardViewHandler);
					if (cardViewHandler.RuntimeCardData is RuntimeEquipmentData)
					{
						RunStatsTracker.Instance.PlayerStatsEntry.RegisterEquipmentEquip(cardViewHandler.RuntimeCardData.BaseData.ID);
					}
					else
					{
						RunStatsTracker.Instance.PlayerStatsEntry.RegisterWeaponEquip(cardViewHandler.RuntimeCardData.BaseData.ID);
					}
					offeringsCanvasGroup.interactable = false;
					await CardsDespawnAnimation();
					DisposeOfferings();
					offeringsCanvasGroup.alpha = 0f;
					EnableBanish(state: false);
					EnableReRoll(state: false);
					EnableDiscard(state: false);
					LockFocusOnHandGroup();
				}
			}
			catch (Exception message)
			{
				Debug.LogError(message);
			}
		}

		public void EnableMenuInteraction(bool state)
		{
			parentGroup.interactable = state;
			parentGroup.blocksRaycasts = state;
			detailsAndContextualContainer.alpha = (state ? 1 : 0);
		}

		private void AddCardViewToOfferingsGroup(UICardViewHandler cardViewHandler, int index)
		{
			_offeringsCards.Add(cardViewHandler);
			cardViewHandler.SetOnDragContainer(onDragContainer);
			cardViewHandler.SetSiblingIndex(index);
			offeringsCardViewContainer.AddCard(cardViewHandler);
			cardViewHandler.CardView.EnableIdleAnimation(state: true);
		}

		private void RemoveCardViewFromOfferingsGroup(UICardViewHandler cardViewHandler)
		{
			_offeringsCards.Remove(cardViewHandler);
			cardViewHandler.SetParentToOnDragContainer();
		}

		private void ReOrderOfferingsGroup()
		{
			for (int i = 0; i < _offeringsCards.Count; i++)
			{
				_offeringsCards[i].SetSiblingIndex(i);
			}
			offeringsLayoutGroup.Refresh();
		}

		private void EnableOfferingCardsInteraction(bool state)
		{
			foreach (UICardViewHandler offeringsCard in _offeringsCards)
			{
				if (state)
				{
					offeringsCard.CardView.EnableMovement();
				}
				else
				{
					offeringsCard.CardView.DisableMovement();
				}
				offeringsCard.AllowInteraction(state);
			}
		}

		private void EnableOfferingsVisibility(bool state)
		{
			if (state && !offeringsCanvasGroup.interactable)
			{
				offeringsCanvasGroup.alpha = 1f;
				offeringsCanvasGroup.interactable = true;
			}
			else if (!state && offeringsCanvasGroup.interactable)
			{
				offeringsCanvasGroup.alpha = 0f;
				offeringsCanvasGroup.interactable = false;
			}
			else
			{
				SetOfferingsGroupUnFocusedDefaultLayout();
			}
		}

		public void SetOfferingsLayoutGroupEnable(bool state)
		{
			if ((bool)offeringsLayoutGroup)
			{
				offeringsLayoutGroup?.Freeze(!state);
			}
		}

		private void SetOfferingsGroupUnFocusedMinimizedLayout()
		{
			offeringFocus.SetUnFocusedParentIndex(1);
			offeringFocus.Refresh();
		}

		private void SetOfferingsGroupUnFocusedDefaultLayout()
		{
			offeringFocus.SetUnFocusedParentIndex(0);
			offeringFocus.Refresh();
		}

		private void SetViewDefaultDropLayout()
		{
			_isWeaponDropLayout = false;
			EnableBanish(state: false);
			banishButton.gameObject.SetActive(value: true);
			EnableDiscard(state: false);
			HandView.FoldHand(instant: true);
			ResetPermanentFocus();
			SetFocusOnOfferings(instant: true);
			EnableOfferingsVisibility(state: false);
		}

		private void SetViewWeaponDropLayout()
		{
			_isWeaponDropLayout = true;
			EnableBanish(state: false);
			EnableDiscard(state: false);
			HandView.FoldHand(instant: true);
			ResetPermanentFocus();
			SetFocusOnOfferings(instant: true);
			EnableOfferingsVisibility(state: false);
			banishButton.gameObject.SetActive(value: false);
		}

		private async Task OpenAnimation()
		{
			await UniTask.WaitForEndOfFrame();
			float fadeDuration = (_isWeaponDropLayout ? openWeaponAnimation.FadeDuration : openEquipmentAnimation.FadeDuration);
			_openCloseAnimationState = animancer.Layers[0].Play(_isWeaponDropLayout ? openWeaponAnimation : openEquipmentAnimation, fadeDuration);
			while (_openCloseAnimationState.IsPlayingAndNotEnding())
			{
				await UniTask.NextFrame();
			}
		}

		private async Task CloseAnimation()
		{
			await UniTask.WaitForEndOfFrame();
			float fadeDuration = (_isWeaponDropLayout ? closeWeaponAnimation.FadeDuration : closeEquipmentAnimation.FadeDuration);
			_openCloseAnimationState = animancer.Layers[0].Play(_isWeaponDropLayout ? closeWeaponAnimation : closeEquipmentAnimation, fadeDuration);
			while (_openCloseAnimationState.IsPlayingAndNotEnding())
			{
				await UniTask.NextFrame();
			}
			parentGroup.alpha = 0f;
			await UniTask.NextFrame();
		}

		private async UniTask CardsSpawnAnimation()
		{
			float spawnMoveTime = animationSettings.SpawnMoveTime;
			float spawnMoveDelay = animationSettings.SpawnMoveDelay;
			CustomAnimationCurve spawnMoveEase = animationSettings.SpawnMoveEase;
			float spawnRotationTime = animationSettings.SpawnRotationTime;
			float spawnRotationDelay = animationSettings.SpawnRotationDelay;
			_cardSpawnSequence?.Kill();
			_cardSpawnSequence = DOTween.Sequence(this);
			RuntimeManager.PlayOneShot(cardOfferingsEnterSound);
			for (int i = 0; i < _offeringsCards.Count; i++)
			{
				UICardViewHandler cardViewHandler = _offeringsCards[i];
				Vector3 resAdjustedScreenSpacePosition = UIResolutionHelpers.GetResAdjustedScreenSpacePosition(new Vector3(960f, -540f));
				Vector3 position = cardViewHandler.Transform.position;
				Vector3 eulerAngles = cardViewHandler.Transform.eulerAngles;
				cardViewHandler.Show();
				cardViewHandler.EnableRarityVFX(state: true);
				cardViewHandler.CardView.DisableMovement();
				cardViewHandler.CardView.DisableTilt(instant: true);
				cardViewHandler.AllowInteraction(value: false);
				cardViewHandler.CardView.Transform.position = resAdjustedScreenSpacePosition;
				cardViewHandler.CardView.Card3DProxy.Card.SetRotation(180f);
				cardViewHandler.CardView.EnableMotionBlur(state: true);
				float atPosition = spawnMoveDelay * (float)i;
				_cardSpawnSequence.Insert(atPosition, cardViewHandler.CardView.Transform.DOMove(position, spawnMoveTime).SetEase(spawnMoveEase.GetEaseFunction()));
				_cardSpawnSequence.Insert(atPosition, cardViewHandler.CardView.Transform.DORotate(eulerAngles, spawnMoveTime));
				_cardSpawnSequence.Insert(atPosition, cardViewHandler.CardView.Card3DProxy.Card.RotateOnPlaceEffect(spawnRotationTime, 0f).SetDelay(spawnRotationDelay));
				_cardSpawnSequence.Insert(atPosition, cardViewHandler.CardView.MotionBlur(5f, 0f, -90f, spawnMoveTime).SetEase(spawnMoveEase.GetEaseFunction()).OnComplete(delegate
				{
					cardViewHandler.CardView.EnableMotionBlur(state: false);
				}));
			}
			_cardSpawnSequence.OnUpdate(delegate
			{
				foreach (UICardViewHandler offeringsCard in _offeringsCards)
				{
					offeringsCard.CardView.DisableTilt();
				}
			});
			_cardSpawnSequence.SetUpdate(UpdateType.Late, isIndependentUpdate: true);
			EnableOfferingsVisibility(state: true);
			await _cardSpawnSequence.AsyncWaitForCompletion();
		}

		private async UniTask CardsDespawnAnimation()
		{
			float despawnMoveTime = animationSettings.DespawnMoveTime;
			CustomAnimationCurve despawnMoveEase = animationSettings.DespawnMoveEase;
			_cardSpawnSequence?.Kill();
			_cardSpawnSequence = DOTween.Sequence(this);
			foreach (UICardViewHandler cardViewHandler in _offeringsCards)
			{
				Vector3 vector = offeringsLayoutGroup.transform.position + Vector3.up * UIResolutionHelpers.GetResAdjustedScreenSpaceOffset(1000f);
				Vector2 vector2 = (vector - cardViewHandler.CardView.Transform.position).normalized;
				float num = Mathf.Atan2(vector2.x, vector2.y) * 57.29578f;
				Vector3 endValue = new Vector3(0f, 0f, 0f - num);
				cardViewHandler.Show();
				cardViewHandler.CardView.DisableMovement();
				cardViewHandler.CardView.EnableMotionBlur(state: true);
				_cardSpawnSequence.Join(cardViewHandler.CardView.Transform.DOMove(vector, despawnMoveTime).SetEase(despawnMoveEase.GetEaseFunction()));
				_cardSpawnSequence.Join(cardViewHandler.CardView.Transform.DORotate(endValue, despawnMoveTime));
				_cardSpawnSequence.Join(cardViewHandler.CardView.MotionBlur(0f, 5f, -90f, despawnMoveTime).SetEase(despawnMoveEase.GetEaseFunction()).OnComplete(delegate
				{
					cardViewHandler.CardView.EnableMotionBlur(state: false);
				}));
			}
			_cardSpawnSequence.SetUpdate(UpdateType.Late, isIndependentUpdate: true);
			await _cardSpawnSequence.AsyncWaitForCompletion();
			EnableOfferingsVisibility(state: false);
			await UniTask.NextFrame();
		}

		private void PlayPoetVA(PoetPoolID poolID)
		{
			if (_visualsGroups.TryGetValue(poolID, out var value) && !value.VALineIds.IsNullOrEmpty())
			{
				// DialogueManager.instance.gameObject.GetComponent<FmodProgramerEventPlayer>().PlayRandomDialogueFromList("event:/sx/dlg/sx_dlg_vo", value.VALineIds, 1f);
			}
		}

		private void SetBackgroundVisuals(PoetPoolID poolID)
		{
			float alpha = (_isWeaponDropLayout ? 1 : 0);
			float alpha2 = ((!_isWeaponDropLayout) ? 1 : 0);
			weaponThorns.alpha = alpha;
			weaponFrame.alpha = alpha;
			equipmentThorns.alpha = alpha2;
			equipmentFrame.alpha = alpha2;
			if (!_visualsGroups.ContainsKey(poolID))
			{
				poolID = PoetPoolID.Dante;
			}
			foreach (KeyValuePair<PoetPoolID, PoetVisualsGroup> visualsGroup in _visualsGroups)
			{
				PoetPoolID key = visualsGroup.Key;
				PoetVisualsGroup value = visualsGroup.Value;
				bool num = key == poolID;
				float alpha3 = (num ? 1 : 0);
				value.Background.alpha = alpha3;
				value.Foreground.alpha = alpha3;
				value.Banner.alpha = alpha3;
				value.Background.interactable = false;
				value.Background.blocksRaycasts = false;
				value.Foreground.interactable = false;
				value.Foreground.blocksRaycasts = false;
				value.Banner.interactable = false;
				value.Banner.blocksRaycasts = false;
				if (!num)
				{
					continue;
				}
				if (_isWeaponDropLayout)
				{
					value.PlateWeaponText.gameObject.SetActive(value: true);
					value.PlateModText.gameObject.SetActive(value: false);
					value.PlateWeaponText.ResetQuote();
					_currentPlateInterpolator = value.PlateWeaponText;
				}
				else
				{
					value.PlateWeaponText.gameObject.SetActive(value: false);
					value.PlateModText.gameObject.SetActive(value: true);
					value.PlateModText.ResetQuote();
					_currentPlateInterpolator = value.PlateModText;
				}
				if (key == PoetPoolID.Dante)
				{
					_currentPlateInterpolator = null;
				}
				if ((bool)value.FrameGemsMaterial)
				{
					Image[] array = weaponFrameGems;
					for (int i = 0; i < array.Length; i++)
					{
						array[i].material = value.FrameGemsMaterial;
					}
				}
			}
		}

		public void RegisterSkipBindings()
		{
			skipGlyph.SetHold(_controller.ConfirmHandHoldTime);
			_controller.OnUICenter2Hold += TrySkipMenu;
			EnableSkipGlyph(state: true);
		}

		public void UnRegisterSkipBindings()
		{
			skipGlyph.SetHold(_controller.ConfirmHandHoldTime);
			_controller.OnUICenter2Hold -= TrySkipMenu;
			EnableSkipGlyph(state: false);
		}

		public void EnableSkipGlyph(bool state)
		{
			skipGlyphContainer.alpha = (state ? 1 : 0);
		}

		public async void TrySkipMenu(float pressedTime)
		{
			if (!(pressedTime < _controller.ConfirmHandHoldTime) && !_controller.IsDraggingCard)
			{
				UnRegisterSkipBindings();
				Close();
			}
		}

		public void EnableReRoll(bool state)
		{
			if (state)
			{
				if (_currentReRollsAmount == 0)
				{
					return;
				}
				RefreshReRollAmountText();
				reRollButton.Show();
				RegisterReRollBindings();
			}
			else
			{
				reRollButton.Hide();
				UnRegisterReRollBindings();
			}
			reRollButton.interactable = state;
		}

		private void RegisterReRollBindings()
		{
			reRollButton.RemoveAllListeners();
			reRollButton.OnEnterCallback += delegate(UICardViewHandler viewHandler)
			{
				StartReRoll(viewHandler, dragMode: true);
			};
			reRollButton.OnDropCallback += StopReRoll;
			reRollButton.OnExitCallback += StopReRoll;
			rerollGlyph.SetHold(_controller.ReRollCardHoldTime);
		}

		private void UnRegisterReRollBindings()
		{
			reRollButton.RemoveAllListeners();
		}

		public void EnableReRollGlyph(bool state)
		{
			rerollGlyph.gameObject.SetActive(state);
		}

		private void RefreshReRollAmountText()
		{
			reRollAmountText.text = _currentReRollsAmount.ToString();
		}

		public async void StartReRoll(UICardViewHandler cardViewHandler, bool dragMode = false)
		{
			if (_currentReRollsAmount == 0 || !CardsInOfferings.FirstOrDefault((UICardViewHandler element) => element.CardView == cardViewHandler.CardView))
			{
				return;
			}
			try
			{
				if (!dragMode)
				{
					EnableMenuInteraction(state: false);
					cardViewHandler.CardView.UnHover();
					cardViewHandler.CardView.EnableSelectionOuterGlow(state: false);
				}
				if (_reRollCTS == null)
				{
					_reRollCTS = new CancellationTokenSource();
				}
				StartHoldSound(ref _reRollHoldInstance, reRollHoldSound);
				float reRollCardHoldTime = Controller.ReRollCardHoldTime;
				List<UniTask> list = new List<UniTask>();
				foreach (UICardViewHandler cardsInOffering in CardsInOfferings)
				{
					cardsInOffering.LockMotion(state: true);
					if (cardViewHandler == cardsInOffering)
					{
						if (dragMode)
						{
							list.Add(cardViewHandler.CardView.PlayReRollDragAnimation(reRollCardHoldTime, reRollCardPivot, _reRollCTS.Token));
						}
						else
						{
							list.Add(cardViewHandler.CardView.PlayReRollAnimation(reRollCardHoldTime, _reRollCTS.Token));
						}
					}
					else
					{
						list.Add(cardsInOffering.CardView.PlayReRollAnimation(reRollCardHoldTime, _reRollCTS.Token));
					}
				}
				await UniTask.WhenAll(list);
				ReleaseHoldSound(ref _reRollHoldInstance);
				_reRollCTS?.Dispose();
				_reRollCTS = null;
				ApplyReRoll();
			}
			catch (OperationCanceledException)
			{
				StopHoldSound(ref _reRollHoldInstance);
				_reRollCTS?.Dispose();
				_reRollCTS = null;
				foreach (UICardViewHandler cardsInOffering2 in CardsInOfferings)
				{
					cardsInOffering2.LockMotion(state: false);
					cardsInOffering2.CardView.StopReRollAnimation();
				}
				if (!dragMode)
				{
					EnableMenuInteraction(state: true);
					cardViewHandler.CardView.EnableSelectionOuterGlow(state: true);
					cardViewHandler.CardView.Hover();
				}
			}
		}

		public void StopReRoll(UICardViewHandler cardViewHandler)
		{
			_reRollCTS?.Cancel();
		}

		public async void ApplyReRoll()
		{
			_ = 3;
			try
			{
				if (_currentReRollsAmount == 0)
				{
					return;
				}
				_currentReRollsAmount--;
				if (!_controller.IsWaitingForPick || _controller.IsSwappingCard)
				{
					return;
				}
				if (!GetReRollCardsData(out var isWeaponDrop))
				{
					Close();
					return;
				}
				_canBanish = true;
				_controller.TransitionToReRolling();
				EnableReRoll(state: false);
				EnableMenuInteraction(state: false);
				if (isWeaponDrop)
				{
					SetViewWeaponDropLayout();
				}
				else
				{
					SetViewDefaultDropLayout();
				}
				await CardsDespawnAnimation();
				await InstantiateCardViews();
				await CardsSpawnAnimation();
				if (!isWeaponDrop)
				{
					await TryShowMergeTutorial();
				}
				EnableMenuInteraction(state: true);
				EnableOfferingCardsInteraction(state: true);
				_controller.TransitionToWaitingPick();
				_controller.ApplyFirstSelection();
			}
			catch (Exception message)
			{
				Debug.LogError(message);
			}
		}

		public void EnableDiscard(bool state)
		{
			if (state)
			{
				discardButton.Show();
				RegisterDiscardButton();
			}
			else
			{
				discardButton.Hide();
				UnRegisterDiscardButton();
			}
			discardButton.interactable = state;
		}

		private void RegisterDiscardButton()
		{
			discardButton.RemoveAllListeners();
			discardButton.OnEnterCallback += delegate(UICardViewHandler viewHandler)
			{
				StartDiscard(viewHandler, dragMode: true);
			};
			discardButton.OnDropCallback += StopDiscard;
			discardButton.OnExitCallback += StopDiscard;
			discardButtonGlyph.SetHoldWithDecrementOvertime(_controller.DiscardCardHoldTime, 2f);
		}

		private void UnRegisterDiscardButton()
		{
			discardButton.RemoveAllListeners();
		}

		public void EnableDiscardGlyph(bool state)
		{
			discardButtonGlyph.gameObject.SetActive(state);
		}

		public async void StartDiscard(UICardViewHandler cardViewHandler, bool dragMode = false)
		{
			_ = 1;
			try
			{
				if (cardViewHandler is UIEquipmentCardViewHandler uIEquipmentCardViewHandler)
				{
					if (_discardCTS == null)
					{
						_discardCTS = new CancellationTokenSource();
					}
					cardViewHandler.LockMotion(state: true);
					EnableMenuInteraction(state: false);
					StartHoldSound(ref _discardHoldInstance, discardHoldSound);
					if (!dragMode)
					{
						await uIEquipmentCardViewHandler.CardView.PlayDiscardFadeAnimation(Controller.DiscardCardHoldTime, _discardCTS.Token);
					}
					else
					{
						await uIEquipmentCardViewHandler.CardView.PlayDiscardDragFadeAnimation(Controller.DiscardCardHoldTime, discardCardPivot, _discardCTS.Token);
					}
					ReleaseHoldSound(ref _discardHoldInstance);
					EnableDiscard(state: false);
					ApplyDiscard(cardViewHandler);
					EnableMenuInteraction(state: true);
					_controller.OnControllerTypeChange();
				}
			}
			catch (OperationCanceledException)
			{
				StopHoldSound(ref _discardHoldInstance);
				_discardCTS?.Dispose();
				_discardCTS = null;
				cardViewHandler.LockMotion(state: false);
				EnableMenuInteraction(state: true);
				cardViewHandler.CardView.StopDiscardFadeAnimation(Controller.DiscardCardHoldTime / 2f);
				EventSystem.current.SetSelectedGameObject(cardViewHandler.gameObject);
			}
			catch (Exception message)
			{
				StopHoldSound(ref _discardHoldInstance);
				Debug.LogError(message);
			}
		}

		public void StopDiscard(UICardViewHandler cardViewHandler)
		{
			_discardCTS?.Cancel();
		}

		private void ApplyDiscard(UICardViewHandler cardViewHandler)
		{
			if (cardViewHandler is UIEquipmentCardViewHandler uIEquipmentCardViewHandler)
			{
				Leveler.Instance.CardPool.UnRegisterChosenCard(uIEquipmentCardViewHandler.RuntimeCardData);
				uIEquipmentCardViewHandler.UnEquip();
				handView.ConstructHandNavigation();
				DestroyCardOnDiscard(uIEquipmentCardViewHandler).Forget();
			}
		}

		private async UniTaskVoid DestroyCardOnDiscard(UIEquipmentCardViewHandler equipmentViewHandler)
		{
			equipmentViewHandler.AllowInteraction(value: false);
			UnityEngine.Object.Destroy(equipmentViewHandler.InputHandler);
			CancellationToken cancellationToken = equipmentViewHandler.CardView.destroyCancellationToken;
			await equipmentViewHandler.CardView.PlayDiscardExplosionParticleSystem().AttachExternalCancellation(cancellationToken);
			if ((bool)equipmentViewHandler && (bool)equipmentViewHandler.gameObject && !cancellationToken.IsCancellationRequested)
			{
				UnityEngine.Object.Destroy(equipmentViewHandler.gameObject);
			}
		}

		public void EnableBanish(bool state)
		{
			if (state)
			{
				if (_currentBanishesAmount == 0 || !_canBanish)
				{
					return;
				}
				RefreshBanishesAmountText();
				banishButton.Show();
				RegisterBanishButton();
			}
			else
			{
				banishButton.Hide();
				UnRegisterBanishButton();
			}
			banishButton.interactable = state;
		}

		private void RegisterBanishButton()
		{
			banishButton.RemoveAllListeners();
			banishButton.OnEnterCallback += delegate(UICardViewHandler viewHandler)
			{
				StartBanish(viewHandler, dragMode: true);
			};
			banishButton.OnDropCallback += StopBanish;
			banishButton.OnExitCallback += StopBanish;
			banishButtonGlyph.SetHoldWithDecrementOvertime(_controller.BanishCardHoldTime, 2f);
		}

		private void UnRegisterBanishButton()
		{
			banishButton.RemoveAllListeners();
		}

		public void EnableBanishGlyph(bool state)
		{
			banishButtonGlyph.gameObject.SetActive(state);
		}

		private void RefreshBanishesAmountText()
		{
			banishesAmountText.text = _currentBanishesAmount.ToString();
		}

		public async void StartBanish(UICardViewHandler cardViewHandler, bool dragMode = false)
		{
			_ = 1;
			try
			{
				if (_canBanish && cardViewHandler is UIEquipmentCardViewHandler uIEquipmentCardViewHandler)
				{
					if (_banishCTS == null)
					{
						_banishCTS = new CancellationTokenSource();
					}
					cardViewHandler.LockMotion(state: true);
					EnableMenuInteraction(state: false);
					StartHoldSound(ref _banishHoldInstance, banishHoldSound);
					if (!dragMode)
					{
						await uIEquipmentCardViewHandler.CardView.PlayBanishFadeAnimation(Controller.BanishCardHoldTime, _banishCTS.Token);
					}
					else
					{
						await uIEquipmentCardViewHandler.CardView.PlayBanishDragFadeAnimation(Controller.BanishCardHoldTime, banishCardPivot, _banishCTS.Token);
					}
					ReleaseHoldSound(ref _banishHoldInstance);
					EnableMenuInteraction(state: true);
					EnableBanish(state: false);
					ApplyBanish(cardViewHandler);
					_controller.OnControllerTypeChange();
				}
			}
			catch (OperationCanceledException)
			{
				StopHoldSound(ref _banishHoldInstance);
				_banishCTS?.Dispose();
				_banishCTS = null;
				cardViewHandler.LockMotion(state: false);
				EnableMenuInteraction(state: true);
				cardViewHandler.CardView.StopBanishFadeAnimation(Controller.BanishCardHoldTime / 2f);
			}
			catch (Exception message)
			{
				StopHoldSound(ref _banishHoldInstance);
				EnableMenuInteraction(state: true);
				Debug.LogError(message);
			}
		}

		public void StopBanish(UICardViewHandler cardViewHandler)
		{
			_banishCTS?.Cancel();
		}

		private void ApplyBanish(UICardViewHandler cardViewHandler)
		{
			if (cardViewHandler is UIEquipmentCardViewHandler uIEquipmentCardViewHandler && _currentBanishesAmount != 0 && _canBanish)
			{
				_currentBanishesAmount--;
				_canBanish = false;
				RefreshBanishesAmountText();
				UnityEngine.Object.Destroy(uIEquipmentCardViewHandler.InputHandler);
				RemoveCardViewFromOfferingsGroup(cardViewHandler);
				cardViewHandler.AllowInteraction(value: false);
				Leveler.Instance.CardPool.BanCard(cardViewHandler.RuntimeCardData.BaseData);
				ReOrderOfferingsGroup();
				DestroyCardOnBanish(uIEquipmentCardViewHandler, base.destroyCancellationToken).Forget();
			}
		}

		private async UniTaskVoid DestroyCardOnBanish(UIEquipmentCardViewHandler equipmentViewHandler, CancellationToken token)
		{
			await equipmentViewHandler.CardView.PlayBanishFadeAnimation(2f, token);
			CancellationToken cancellationToken = equipmentViewHandler.CardView.destroyCancellationToken;
			await equipmentViewHandler.CardView.PlayBanishExplosionParticleSystem().AttachExternalCancellation(cancellationToken);
			if ((bool)equipmentViewHandler && (bool)equipmentViewHandler.gameObject && !cancellationToken.IsCancellationRequested)
			{
				UnityEngine.Object.Destroy(equipmentViewHandler.gameObject);
			}
		}

		private void UnSelectEventSystemObject()
		{
			EventSystem.current.SetSelectedGameObject(null);
		}

		public void SetFocusOnHand(bool instant = false)
		{
			if (instant)
			{
				SetActiveFocusInstant(handFocus);
			}
			else
			{
				SetActiveFocus(handFocus);
			}
			offeringsLayoutGroup.AngleMultiplier = 0f;
			offeringsLayoutGroup.MaxOffset = 0f;
			offeringsCardViewContainerCanvas.overrideSorting = false;
		}

		public void LockFocusOnHandGroup()
		{
			SetFocusOnHand();
			selectedFocusElement.permanentFocus = true;
		}

		public void SetFocusOnOfferings(bool instant = false)
		{
			if (instant)
			{
				SetActiveFocusInstant(offeringFocus);
			}
			else
			{
				SetActiveFocus(offeringFocus);
			}
			offeringsLayoutGroup.AngleMultiplier = _defaultAngleMultiplier;
			offeringsLayoutGroup.MaxOffset = _defaultMaxOffset;
			offeringsCardViewContainerCanvas.overrideSorting = true;
		}

		public void ResetPermanentFocus()
		{
			offeringFocus.permanentFocus = false;
			handFocus.permanentFocus = false;
		}

		public void SwitchFocusGroup(UIFocusParentSwitcherElement focusElement)
		{
			if (focusElement == HandFocus)
			{
				if (!HandView.HasWeapons)
				{
					if (_controller.IsDraggingCard)
					{
						SetFocusOnHand();
					}
				}
				else
				{
					SetFocusOnHand();
				}
			}
			else
			{
				SetFocusOnOfferings();
			}
		}

		public void SetActiveFocus(UIFocusParentSwitcherElement focusElement)
		{
			if (!selectedFocusElement.permanentFocus && !(selectedFocusElement == focusElement))
			{
				if (focusElement != selectedFocusElement)
				{
					selectedFocusElement.UnFocus();
					selectedFocusElement = focusElement;
				}
				focusElement.Focus();
			}
		}

		public void SetActiveFocusInstant(UIFocusParentSwitcherElement focusElement)
		{
			if (!selectedFocusElement.permanentFocus && !(selectedFocusElement == focusElement))
			{
				if (focusElement != selectedFocusElement)
				{
					selectedFocusElement.UnFocus();
					selectedFocusElement.OnUnFocusEnterInstant();
					selectedFocusElement = focusElement;
				}
				focusElement.Focus();
				focusElement.OnFocusEnterInstant();
			}
		}

		public void HandEnterSound()
		{
			RuntimeManager.PlayOneShot(cardHandEnterSound);
		}

		private void StartHoldSound(ref EventInstance instance, EventReference soundRef)
		{
			if (!soundRef.IsNull)
			{
				StopHoldSound(ref instance);
				instance = RuntimeManager.CreateInstance(soundRef);
				instance.start();
			}
		}

		private void StopHoldSound(ref EventInstance instance)
		{
			if (instance.isValid())
			{
				instance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
				instance.release();
				instance.clearHandle();
			}
		}

		private void ReleaseHoldSound(ref EventInstance instance)
		{
			if (instance.isValid())
			{
				instance.release();
				instance.clearHandle();
			}
		}

		private async UniTask TryShowCardsTutorial()
		{
			if (IsWeaponDropLayout)
			{
				if (!(await TutorialManager.Instance.CPM.TryLaunchWeaponCardsTutorial()) && Leveler.Instance.CardPool.WeaponDropLevels[0] == Leveler.Instance.Level)
				{
					await TutorialManager.Instance.CPM.TryLaunchHandManagementCardsTutorial();
				}
			}
			else if (!(await TutorialManager.Instance.CPM.TryLaunchModCardsTutorial()))
			{
				await TryShowMergeTutorial();
			}
		}

		private async UniTask TryShowMergeTutorial()
		{
			if (Leveler.Instance.Level <= 1 || GameDataManager.GetGameTriggerState("Tutorial_MergingCards"))
			{
				return;
			}
			foreach (UICardViewHandler offeringsCard in _offeringsCards)
			{
				foreach (PlayerHandSlot slot in PlayerHand.Instance.Slots)
				{
					if (slot.GetPotentialMergeCount(offeringsCard.RuntimeCardData as RuntimeEquipmentData) != 0)
					{
						await TutorialManager.Instance.CPM.TryLaunchMergingCardsTutorial();
						return;
					}
				}
			}
		}

		public void EnableContextualGlyphs(bool state)
		{
			contextualGlyphsContainer.gameObject.SetActive(state);
			if (!state)
			{
				ShowDetailsGlyph(state: false);
				ShowDropCardGlyph(state: false);
				ShowEquipAcceptGlyph(state: false);
				ShowEquipCancelGlyph(state: false);
				ShowSwapCardAcceptGlyph(state: false);
				ShowSwapCardCancelGlyph(state: false);
				ShowSlotSwapModeAcceptGlyph(state: false);
				ShowSlotSwapModeCancelGlyph(state: false);
				ShowSlotSwapAcceptGlyph(state: false);
			}
		}

		public void ShowDetailsGlyph(bool state)
		{
			detailsMenuGlyph.gameObject.SetActive(state);
		}

		public void ShowDropCardGlyph(bool state)
		{
			dropCardAcceptGlyph.gameObject.SetActive(state);
			LayoutRebuilder.MarkLayoutForRebuild(contextualGlyphsContainer);
		}

		public void ShowEquipAcceptGlyph(bool state)
		{
			equipCardAcceptGlyph.gameObject.SetActive(state);
			LayoutRebuilder.MarkLayoutForRebuild(contextualGlyphsContainer);
		}

		public void ShowEquipCancelGlyph(bool state)
		{
			equipCardCancelGlyph.gameObject.SetActive(state);
			LayoutRebuilder.MarkLayoutForRebuild(contextualGlyphsContainer);
		}

		public void ShowSwapCardAcceptGlyph(bool state)
		{
			swapCardAcceptGlyph.gameObject.SetActive(state);
			LayoutRebuilder.MarkLayoutForRebuild(contextualGlyphsContainer);
		}

		public void ShowSwapCardCancelGlyph(bool state)
		{
			swapCardCancelGlyph.gameObject.SetActive(state);
			LayoutRebuilder.MarkLayoutForRebuild(contextualGlyphsContainer);
		}

		public void ShowSlotSwapModeAcceptGlyph(bool state)
		{
			swapHandSlotModeAcceptGlyph.gameObject.SetActive(state);
			LayoutRebuilder.MarkLayoutForRebuild(contextualGlyphsContainer);
		}

		public void ShowSlotSwapModeCancelGlyph(bool state)
		{
			swapHandSlotModeCancelGlyph.gameObject.SetActive(state);
			LayoutRebuilder.MarkLayoutForRebuild(contextualGlyphsContainer);
		}

		public void ShowSlotSwapAcceptGlyph(bool state)
		{
			swapHandSlotAcceptGlyph.gameObject.SetActive(state);
			LayoutRebuilder.MarkLayoutForRebuild(contextualGlyphsContainer);
		}
	}
}
