using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Animancer;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Controllers;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.UI.Cards;
using AstralShift.HellMaiden.UI.Menus.PauseMenu;
using AstralShift.Helpers;
using AstralShift.Managers;
using AstralShift.UI;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using FMODUnity;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI.Menus
{
	public class WeaponSelectionMenuView : MonoBehaviour
	{
		public static WeaponSelectionMenuView Instance;

		[SerializeField]
		private WeaponSelectionLayouts weaponSelectionLayouts;

		[SerializeField]
		private WeaponSelectionMenuController _controller;

		[SerializeField]
		protected WSMCardSlotViewHandler emptySlotPrefab;

		[Header("References")]
		[SerializeField]
		protected CanvasGroup parentGroup;

		[SerializeField]
		protected WSMCardViewHandlerContainer cardsLayoutGroup;

		[SerializeField]
		protected WSMCardViewHandlerContainerNavigation cardsLayoutGroupNavigation;

		[SerializeField]
		private CanvasGroup cardsCanvasGroup;

		[SerializeField]
		protected WSMCardViewContainer cardViewsContainer;

		[SerializeField]
		private RectTransform weapon3DViewContainer;

		[SerializeField]
		protected GraphicRaycaster scrollButtonRaycaster;

		[SerializeField]
		protected UIFadableButton scrollRightButton;

		[SerializeField]
		protected UIFadableButton scrollLeftButton;

		[SerializeField]
		protected CardInformationPanel cardsInfoPanel;

		[SerializeField]
		protected CanvasGroup cardsInfoPanelCanvasGroup;

		[SerializeField]
		protected CanvasGroup cardsInfoPanelTextCanvasGroup;

		[Header("Animations")]
		[SerializeField]
		protected WSMAnimationSettings animationSettings;

		[Space]
		[SerializeField]
		protected AnimancerComponent animancer;

		[SerializeField]
		protected ClipTransition openAnimation;

		[SerializeField]
		protected ClipTransition closeAnimation;

		[SerializeField]
		protected float closeDelay = 0.1f;

		[Space]
		[SerializeField]
		protected AnimancerComponent infoPanelAnimancer;

		[SerializeField]
		protected ClipTransition infoPanelOpenAnimation;

		[SerializeField]
		protected ClipTransition infoPanelCloseAnimation;

		private bool _isInfoPanelOpen;

		[Header("VFX")]
		[SerializeField]
		protected Image lightRaysIn;

		[SerializeField]
		protected Image lightRaysOut;

		[SerializeField]
		protected Image frameGemsLeft;

		[SerializeField]
		protected Image frameGemsRight;

		[Header("Sound")]
		[SerializeField]
		private EventReference activateSound;

		[SerializeField]
		private EventReference menuInSound;

		[SerializeField]
		private EventReference menuOutSound;

		[SerializeField]
		private EventReference cardInfoPanelInSound;

		[SerializeField]
		private EventReference cardInfoPanelOutSound;

		[SerializeField]
		private EventReference cardRevealSound;

		[SerializeField]
		private EventReference cardSelectedSound;

		[SerializeField]
		private EventReference cardChangedSound;

		private RuntimeWeaponData[] _availableCardsData;

		private List<UICardViewHandler> _availableCardViews;

		private List<WSMCardSlotViewHandler> _emptySlotViews;

		private List<Transform> _allSlotsTransforms;

		private Dictionary<uint, WSMWeapon3DView> _weapon3DViews;

		private WSMWeapon3DView _currentWeapon3DView;

		private bool _isScrolling;

		private Tween _cardsInfoPanelTween;

		private AnimancerState _openCloseAnimationState;

		private AnimancerState _infoPanelOpenCloseAnimationState;

		private Sequence _cardSpawnSequence;

		private Sequence _lightRayFadeTween;

		public WeaponSelectionMenuController Controller => _controller;

		public bool IsInteractable => parentGroup.interactable;

		public WSMCardViewHandlerContainer CardsLayoutGroup => cardsLayoutGroup;

		public WSMCardViewHandlerContainerNavigation CardsLayoutGroupNavigation => cardsLayoutGroupNavigation;

		public RectTransform Weapon3DViewContainer => weapon3DViewContainer;

		public event Action OnClose;

		private void Awake()
		{
			Instance = this;
			RecoverSameObjectReferences();
			_availableCardViews = new List<UICardViewHandler>();
			_emptySlotViews = new List<WSMCardSlotViewHandler>();
			_allSlotsTransforms = new List<Transform>();
			_weapon3DViews = new Dictionary<uint, WSMWeapon3DView>();
			ControllerManager.Instance.Subscribe(_controller);
			parentGroup.alpha = 0f;
			cardsInfoPanelCanvasGroup.alpha = 0f;
			HideInfoPanel();
			animancer.UpdateMode = AnimatorUpdateMode.UnscaledTime;
			infoPanelAnimancer.UpdateMode = AnimatorUpdateMode.UnscaledTime;
			EnableMenuInteraction(state: false);
			HideLightRay();
		}

		private void OnDestroy()
		{
			if (ControllerManager.Instance != null && _controller != null)
			{
				ControllerManager.Instance.UnSubscribe(_controller);
			}
		}

		private void RecoverSameObjectReferences()
		{
			if (_controller == null)
			{
				_controller = GetComponent<WeaponSelectionMenuController>();
			}
			if (parentGroup == null)
			{
				parentGroup = GetComponent<CanvasGroup>();
			}
			if (animancer == null)
			{
				animancer = GetComponent<AnimancerComponent>();
			}
		}

		public async void Open()
		{
			_ = 4;
			try
			{
				_availableCardsData = GetAllSignatureWeapons();
				OverrideGameController();
				cardViewsContainer.Hide();
				await UniTask.WhenAll(InstantiateAllViews(), OpenAnimation());
				await UniTask.Delay(TimeSpan.FromSeconds(0.10000000149011612), ignoreTimeScale: true);
				cardViewsContainer.Show();
				await CardsSpawnAnimation();
				EnableCardsMovement(state: true);
				EnableSlotsMovement(state: true);
				await UniTask.NextFrame();
				await TutorialManager.Instance.SSM.TryLaunchSignatureCardsTutorial(_controller.TransitionToWaitingForSelection);
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		public async void Close()
		{
			try
			{
				this.OnClose?.Invoke();
				await CloseAnimation();
				DisposeCards();
				DisposeSlots();
				YieldGameController();
				UnSelectEventSystemObject();
				HideLightRay();
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		private void OverrideGameController()
		{
			_controller = ControllerManager.Instance.OverrideGameController<WeaponSelectionMenuController>();
		}

		private void YieldGameController()
		{
			ControllerManager.Instance.YieldGameController();
		}

		private async UniTask InstantiateAllViews()
		{
			List<Task<Transform>> allInstantiationTasks = new List<Task<Transform>>();
			for (int i = 0; i < _availableCardsData.Length; i++)
			{
				RuntimeWeaponData runtimeWeaponData = _availableCardsData[i];
				if (runtimeWeaponData != null)
				{
					allInstantiationTasks.Add(InstantiateCard(runtimeWeaponData));
				}
			}
			if (_availableCardsData.Length < cardsLayoutGroup.ElementsCount)
			{
				int num = cardsLayoutGroup.ElementsCount - _availableCardsData.Length;
				for (int j = 0; j < num; j++)
				{
					allInstantiationTasks.Add(InstantiateEmptySlot());
				}
			}
			await Task.WhenAll(allInstantiationTasks);
			CardsLayoutGroup.Refresh();
			_allSlotsTransforms.Clear();
			allInstantiationTasks.ForEach(delegate(Task<Transform> element)
			{
				_allSlotsTransforms.Add(element.Result);
			});
			SortSlots(_allSlotsTransforms);
			uint currentWeaponID = GameDataManager.Instance.GetSignatureWeaponID();
			UICardViewHandler uICardViewHandler = _availableCardViews.Find((UICardViewHandler view) => view is UIWeaponCardViewHandler uIWeaponCardViewHandler && uIWeaponCardViewHandler.RuntimeWeaponData.BaseData.ID == currentWeaponID);
			CardsLayoutGroup.Refresh();
			if (uICardViewHandler != null)
			{
				cardsLayoutGroup.CenterOnTransform(uICardViewHandler.Transform);
			}
		}

		private async Task<Transform> InstantiateCard(RuntimeWeaponData data)
		{
			UICardViewHandler newCardViewHandler = await CardVisualsFactory.GetUICard(data, cardsLayoutGroup.transform);
			newCardViewHandler.gameObject.AddComponent<WSMCardViewGamepadHandler>();
			newCardViewHandler.gameObject.AddComponent<WSMCardViewMouseHandler>();
			newCardViewHandler.Hide();
			newCardViewHandler.AllowInteraction(value: false);
			newCardViewHandler.CardView.DisableMovement();
			AddCardViewToLayoutGroup(newCardViewHandler, cardsLayoutGroup.ElementsCount);
			await TryInstantiateWeapon3DView(data.Data);
			return newCardViewHandler.Transform;
		}

		private async Task<Transform> InstantiateEmptySlot()
		{
			AsyncInstantiateOperation<WSMCardSlotViewHandler> instantiateOp = UnityEngine.Object.InstantiateAsync(emptySlotPrefab, cardsLayoutGroup.transform);
			await instantiateOp;
			WSMCardSlotViewHandler emptySlot = instantiateOp.Result[0];
			await emptySlot.InitializeAsync();
			emptySlot.Hide();
			emptySlot.AllowInteraction(value: false);
			emptySlot.SlotView.DisableMovement();
			AddEmptySlotToLayoutGroup(emptySlot, cardsLayoutGroup.ElementsCount);
			return emptySlot.Transform;
		}

		private async UniTask TryInstantiateWeapon3DView(WeaponData data)
		{
			WeaponSelectionLayoutEntry entry;
			if (_weapon3DViews.TryGetValue(data.ID, out var value))
			{
				value.Hide();
			}
			else if (!weaponSelectionLayouts.TryGetEntry(data, out entry))
			{
				Debug.LogWarning("No WeaponSelectionLayoutEntry found for weapon: " + data.name);
			}
			else if ((bool)entry.Weapon3DViewPrefab)
			{
				AsyncInstantiateOperation<WSMWeapon3DView> instantiateOp = UnityEngine.Object.InstantiateAsync(entry.Weapon3DViewPrefab, Weapon3DViewContainer);
				await instantiateOp;
				value = instantiateOp.Result[0];
				value.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
				value.Hide();
				value.Initialize();
				_weapon3DViews.Add(data.ID, value);
			}
		}

		private void SetFocusedWeapon3DView(WeaponData weaponData)
		{
			HideWeapon3DView();
			if (_weapon3DViews.TryGetValue(weaponData.ID, out var value))
			{
				_currentWeapon3DView = value;
				ShowWeapon3DView();
			}
		}

		private void ShowWeapon3DView()
		{
			_currentWeapon3DView?.Show();
		}

		private void HideWeapon3DView()
		{
			_currentWeapon3DView?.Hide();
		}

		private void SortSlots(List<Transform> slotsTransforms)
		{
			bool flag = true;
			for (int i = 0; i < slotsTransforms.Count; i++)
			{
				CardsLayoutGroup.TryGetViewHandlerOfTransform(slotsTransforms[i], out var resultViewHandler, out var resultSlotViewHandler);
				if ((bool)resultViewHandler)
				{
					resultViewHandler.SetSiblingIndex(flag ? CardsLayoutGroup.ChildrenTransforms.Count : 0);
				}
				if ((bool)resultSlotViewHandler)
				{
					resultSlotViewHandler.SetSiblingIndex(flag ? CardsLayoutGroup.ChildrenTransforms.Count : 0);
				}
				if (i != 0)
				{
					flag = !flag;
				}
			}
		}

		private void DisposeCards()
		{
			for (int i = 0; i < _availableCardViews.Count; i++)
			{
				UnityEngine.Object.Destroy(_availableCardViews[i].gameObject);
			}
			_availableCardViews.Clear();
		}

		private void DisposeSlots()
		{
			for (int i = 0; i < _emptySlotViews.Count; i++)
			{
				UnityEngine.Object.Destroy(_emptySlotViews[i].gameObject);
			}
			_emptySlotViews.Clear();
		}

		public void EnableMenuInteraction(bool state)
		{
			parentGroup.interactable = state;
			parentGroup.blocksRaycasts = state;
			scrollButtonRaycaster.enabled = state;
		}

		private void AddCardViewToLayoutGroup(UICardViewHandler cardViewHandler, int index)
		{
			_availableCardViews.Add(cardViewHandler);
			cardViewHandler.SetSiblingIndex(index);
			cardViewsContainer.AddCard(cardViewHandler);
		}

		private void AddEmptySlotToLayoutGroup(WSMCardSlotViewHandler slotViewHandler, int index)
		{
			_emptySlotViews.Add(slotViewHandler);
			slotViewHandler.SetSiblingIndex(index);
			cardViewsContainer.AddSlot(slotViewHandler);
		}

		private void EnableCardsMovement(bool state)
		{
			foreach (UICardViewHandler availableCardView in _availableCardViews)
			{
				if (state)
				{
					availableCardView.CardView.EnableMovement();
					availableCardView.CardView.EnableIdleAnimation(state: true);
				}
				else
				{
					availableCardView.CardView.DisableMovement();
				}
			}
		}

		private void EnableSlotsMovement(bool state)
		{
			foreach (WSMCardSlotViewHandler emptySlotView in _emptySlotViews)
			{
				if (state)
				{
					emptySlotView.SlotView.EnableMovement();
					emptySlotView.SlotView.EnableIdleAnimation(state: true);
				}
				else
				{
					emptySlotView.SlotView.DisableMovement();
				}
			}
		}

		private void EnableCardsLayoutVisibility(bool state)
		{
			cardsCanvasGroup.alpha = (state ? 1 : 0);
			cardsCanvasGroup.interactable = state;
		}

		private async UniTask ScrollToRight()
		{
			EnableAllCardsStaticOpt();
			RuntimeManager.PlayOneShot(cardChangedSound);
			CardsLayoutGroup.ScrollToRight();
			UnRegisterScrollBindings();
			_isScrolling = true;
			await UniTask.Delay(TimeSpan.FromSeconds(0.4000000059604645), ignoreTimeScale: true);
			SelectFocusedWeapon();
			RegisterScrollBindings();
			_isScrolling = false;
		}

		private async UniTask ScrollToLeft()
		{
			EnableAllCardsStaticOpt();
			RuntimeManager.PlayOneShot(cardChangedSound);
			CardsLayoutGroup.ScrollToLeft();
			UnRegisterScrollBindings();
			_isScrolling = true;
			await UniTask.Delay(TimeSpan.FromSeconds(0.4000000059604645), ignoreTimeScale: true);
			SelectFocusedWeapon();
			RegisterScrollBindings();
			_isScrolling = false;
		}

		public async void SelectFocusedWeapon()
		{
			try
			{
				CardsLayoutGroup.SelectFocusedElement();
				if (CardsLayoutGroup.TryGetFocusedCard(out var cardViewHandler))
				{
					TrySetWeaponLayoutVisuals(cardViewHandler);
					RefreshInfoPanel(cardViewHandler.RuntimeCardData.BaseData as WeaponData);
					cardViewHandler.CardView.EnableStaticRender(state: false);
				}
				else
				{
					HideLightRay();
					HideWeapon3DView();
					await InfoPanelCloseAnimation();
				}
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		public async void ChooseWeapon(UIWeaponCardViewHandler viewHandler)
		{
			_ = 3;
			try
			{
				if (!_isScrolling)
				{
					RuntimeManager.PlayOneShot(cardSelectedSound);
					_controller.TransitionToClose();
					PlayerHand.Instance.SetSignatureWeapon(viewHandler.RuntimeWeaponData.Data);
					GameDataManager.Instance.RegisterSignatureWeaponID(viewHandler.RuntimeWeaponData.Data.ID);
					GameDataManager.Instance.SaveGameData();
					HideInfoPanel();
					viewHandler.CardView.LockAllMotion();
					await viewHandler.CardView.EquipEffect();
					await UniTask.Delay(100, ignoreTimeScale: true);
					await viewHandler.CardView.Sheen(0.33f).AsyncWaitForCompletion();
					await UniTask.Delay((int)(closeDelay * 1000f), ignoreTimeScale: true);
					Close();
				}
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		public void ToggleInfoPanel()
		{
			if (_isInfoPanelOpen)
			{
				HideInfoPanel();
			}
			else
			{
				ShowInfoPanel();
			}
		}

		private UniTask ShowInfoPanel()
		{
			if (!_isInfoPanelOpen)
			{
				RuntimeManager.PlayOneShot(cardInfoPanelInSound);
			}
			_isInfoPanelOpen = true;
			return InfoPanelOpenAnimation();
		}

		private UniTask HideInfoPanel()
		{
			if (_isInfoPanelOpen)
			{
				RuntimeManager.PlayOneShot(cardInfoPanelOutSound);
			}
			_isInfoPanelOpen = false;
			return InfoPanelCloseAnimation();
		}

		private async void RefreshInfoPanel(WeaponData data)
		{
			try
			{
				_cardsInfoPanelTween?.Kill();
				_cardsInfoPanelTween = cardsInfoPanelTextCanvasGroup.DOFade(0f, animationSettings.InfoPanelTextFadeDuration / 2f).SetUpdate(UpdateType.Late, isIndependentUpdate: true);
				await _cardsInfoPanelTween.AsyncWaitForCompletion();
				cardsInfoPanel.ShowSignatureWeaponStatsText(data);
				_cardsInfoPanelTween = cardsInfoPanelTextCanvasGroup.DOFade(1f, animationSettings.InfoPanelTextFadeDuration / 2f).SetUpdate(UpdateType.Late, isIndependentUpdate: true);
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		private void EnableAllCardsStaticOpt()
		{
			foreach (UICardViewHandler availableCardView in _availableCardViews)
			{
				availableCardView.CardView.EnableStaticRender(state: true);
			}
		}

		private async UniTask OpenAnimation()
		{
			await UniTask.WaitForEndOfFrame();
			_openCloseAnimationState = animancer.Layers[0].Play(openAnimation, openAnimation.FadeDuration);
			while (_openCloseAnimationState.IsPlayingAndNotEnding())
			{
				await UniTask.NextFrame();
			}
		}

		private async UniTask CloseAnimation()
		{
			await UniTask.WaitForEndOfFrame();
			_openCloseAnimationState = animancer.Layers[0].Play(closeAnimation, closeAnimation.FadeDuration);
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
			RuntimeManager.PlayOneShot(cardRevealSound);
			for (int i = 0; i < CardsLayoutGroup.ChildrenTransforms.Count; i++)
			{
				CardsLayoutGroup.TryGetViewHandlerOfIndex(i, out var resultCardViewHandler, out var resultSlotViewHandler);
				if (i > CardsLayoutGroup.ElementsCount - 1)
				{
					if ((bool)resultCardViewHandler)
					{
						resultCardViewHandler.Show();
						resultCardViewHandler.EnableRarityVFX(state: true);
						resultCardViewHandler.CardView.EnableMovement();
						continue;
					}
					if ((bool)resultSlotViewHandler)
					{
						resultSlotViewHandler.Show();
						resultSlotViewHandler.SlotView.EnableMovement();
						continue;
					}
				}
				if ((bool)resultCardViewHandler)
				{
					Vector3 resAdjustedScreenSpacePosition = UIResolutionHelpers.GetResAdjustedScreenSpacePosition(new Vector3(960f, -540f));
					Vector3 position = resultCardViewHandler.Transform.position;
					Vector3 eulerAngles = resultCardViewHandler.Transform.eulerAngles;
					resultCardViewHandler.Show();
					resultCardViewHandler.EnableRarityVFX(state: true);
					resultCardViewHandler.CardView.DisableMovement();
					resultCardViewHandler.AllowInteraction(value: false);
					resultCardViewHandler.CardView.Transform.position = resAdjustedScreenSpacePosition;
					resultCardViewHandler.CardView.Card3DProxy.Card.SetRotation(180f);
					resultCardViewHandler.CardView.EnableMotionBlur(state: true);
					float atPosition = spawnMoveDelay * (float)i;
					_cardSpawnSequence.Insert(atPosition, resultCardViewHandler.CardView.Transform.DOMove(position, spawnMoveTime).SetEase(spawnMoveEase.GetEaseFunction()));
					_cardSpawnSequence.Insert(atPosition, resultCardViewHandler.CardView.Transform.DORotate(eulerAngles, spawnMoveTime));
					_cardSpawnSequence.Insert(atPosition, resultCardViewHandler.CardView.Card3DProxy.Card.RotateOnPlaceEffect(spawnRotationTime, 0f).SetDelay(spawnRotationDelay));
					_cardSpawnSequence.Insert(atPosition, resultCardViewHandler.CardView.MotionBlur(5f, 0f, -90f, spawnMoveTime).SetEase(spawnMoveEase.GetEaseFunction()).OnComplete(delegate
					{
						resultCardViewHandler.CardView.EnableMotionBlur(state: false);
					}));
				}
				else if ((bool)resultSlotViewHandler)
				{
					Vector3 resAdjustedScreenSpacePosition2 = UIResolutionHelpers.GetResAdjustedScreenSpacePosition(new Vector3(960f, -540f));
					Vector3 position2 = resultSlotViewHandler.Transform.position;
					Vector3 eulerAngles2 = resultSlotViewHandler.Transform.eulerAngles;
					resultSlotViewHandler.Show();
					resultSlotViewHandler.SlotView.DisableMovement();
					resultSlotViewHandler.AllowInteraction(value: false);
					resultSlotViewHandler.SlotView.Transform.position = resAdjustedScreenSpacePosition2;
					resultSlotViewHandler.SlotView.EnableMotionBlur(state: true);
					float atPosition = spawnMoveDelay * (float)i;
					_cardSpawnSequence.Insert(atPosition, resultSlotViewHandler.SlotView.Transform.DOMove(position2, spawnMoveTime).SetEase(spawnMoveEase.GetEaseFunction()));
					_cardSpawnSequence.Insert(atPosition, resultSlotViewHandler.SlotView.Transform.DORotate(eulerAngles2, spawnMoveTime));
					_cardSpawnSequence.Insert(atPosition, resultSlotViewHandler.SlotView.MotionBlur(5f, 0f, -90f, spawnMoveTime).SetEase(spawnMoveEase.GetEaseFunction()).OnComplete(delegate
					{
						resultSlotViewHandler.SlotView.EnableMotionBlur(state: false);
					}));
				}
			}
			_cardSpawnSequence.SetUpdate(UpdateType.Late, isIndependentUpdate: true);
			EnableCardsLayoutVisibility(state: true);
			await _cardSpawnSequence.AsyncWaitForCompletion();
		}

		private async UniTask CardsDespawnAnimation()
		{
			EnableCardsLayoutVisibility(state: false);
		}

		private async UniTask InfoPanelOpenAnimation()
		{
			if (!(_infoPanelOpenCloseAnimationState?.Clip == infoPanelOpenAnimation.Clip))
			{
				cardsInfoPanelCanvasGroup.alpha = 1f;
				await UniTask.WaitForEndOfFrame();
				_infoPanelOpenCloseAnimationState = infoPanelAnimancer.Layers[0].Play(infoPanelOpenAnimation, infoPanelOpenAnimation.FadeDuration);
				while (_infoPanelOpenCloseAnimationState.IsPlayingAndNotEnding())
				{
					await UniTask.NextFrame();
				}
			}
		}

		private async UniTask InfoPanelCloseAnimation()
		{
			if (!(_infoPanelOpenCloseAnimationState?.Clip == infoPanelCloseAnimation.Clip))
			{
				await UniTask.WaitForEndOfFrame();
				_infoPanelOpenCloseAnimationState = infoPanelAnimancer.Layers[0].Play(infoPanelCloseAnimation, infoPanelCloseAnimation.FadeDuration);
				while (_infoPanelOpenCloseAnimationState.IsPlayingAndNotEnding())
				{
					await UniTask.NextFrame();
				}
				cardsInfoPanelCanvasGroup.alpha = 0f;
				EnableAllCardsStaticOpt();
				await UniTask.NextFrame();
			}
		}

		private void TrySetWeaponLayoutVisuals(UICardViewHandler cardViewHandler)
		{
			if (cardViewHandler is UIWeaponCardViewHandler uIWeaponCardViewHandler)
			{
				SetFocusedWeapon3DView(uIWeaponCardViewHandler.RuntimeWeaponData.Data);
				ShowLightRay();
				SetLightRayColor(uIWeaponCardViewHandler.RuntimeWeaponData.Data);
				SetFrameGemsColor(uIWeaponCardViewHandler.RuntimeWeaponData.Data);
			}
			else
			{
				HideLightRay();
				HideWeapon3DView();
			}
		}

		private void ShowLightRay()
		{
			_lightRayFadeTween?.Kill();
			_lightRayFadeTween = DOTween.Sequence(this);
			_lightRayFadeTween.Append(lightRaysIn.DOFade(1f, animationSettings.LightRaysFadeDuration));
			_lightRayFadeTween.Join(lightRaysOut.DOFade(1f, animationSettings.LightRaysFadeDuration));
			_lightRayFadeTween.SetUpdate(UpdateType.Late, isIndependentUpdate: true);
		}

		private void HideLightRay()
		{
			_lightRayFadeTween?.Kill();
			_lightRayFadeTween = DOTween.Sequence(this);
			_lightRayFadeTween.Append(lightRaysIn.DOFade(0f, animationSettings.LightRaysFadeDuration));
			_lightRayFadeTween.Join(lightRaysOut.DOFade(0f, animationSettings.LightRaysFadeDuration));
			_lightRayFadeTween.SetUpdate(UpdateType.Late, isIndependentUpdate: true);
		}

		private void SetLightRayColor(WeaponData data)
		{
			if (Application.isPlaying)
			{
				if (!weaponSelectionLayouts.TryGetEntry(data, out var entry))
				{
					Debug.LogWarning("No WeaponSelectionLayoutEntry found for weapon: " + data.name);
					return;
				}
				lightRaysIn.material = entry.WSMLightRayInnerMaterial;
				lightRaysOut.material = entry.WSMLightRayOuterMaterial;
			}
		}

		private void SetFrameGemsColor(WeaponData data)
		{
			if (Application.isPlaying)
			{
				if (!weaponSelectionLayouts.TryGetEntry(data, out var entry))
				{
					Debug.LogWarning("No WeaponSelectionLayoutEntry found for weapon: " + data.name);
					return;
				}
				frameGemsLeft.material = entry.WSMFrameGemsMaterial;
				frameGemsRight.material = entry.WSMFrameGemsMaterial;
			}
		}

		public void MenuInSound()
		{
			RuntimeManager.PlayOneShot(menuInSound);
		}

		public void MenuOutSound()
		{
			RuntimeManager.PlayOneShot(menuOutSound);
		}

		public void RegisterScrollBindings()
		{
			UnRegisterScrollBindings();
			if (!CardsLayoutGroup.IsLeftSlotEmpty())
			{
				scrollLeftButton.Show();
				scrollLeftButton.Button.onClick.AddListener(delegate
				{
					ScrollToLeft();
				});
				_controller.OnUIDirectionalLeftPressed += InvokeScrollLeftButtonClick;
			}
			if (!CardsLayoutGroup.IsRightSlotEmpty())
			{
				scrollRightButton.Show();
				scrollRightButton.Button.onClick.AddListener(delegate
				{
					ScrollToRight();
				});
				_controller.OnUIDirectionalRightPressed += InvokeScrollRightButtonClick;
			}
		}

		public void UnRegisterScrollBindings()
		{
			scrollRightButton.Hide();
			scrollLeftButton.Hide();
			scrollRightButton.Button.onClick.RemoveAllListeners();
			scrollLeftButton.Button.onClick.RemoveAllListeners();
			_controller.OnUIDirectionalLeftPressed -= InvokeScrollLeftButtonClick;
			_controller.OnUIDirectionalRightPressed -= InvokeScrollRightButtonClick;
		}

		private UniTask InvokeScrollLeftButtonClick()
		{
			scrollLeftButton.SimulateClick();
			return UniTask.CompletedTask;
		}

		private UniTask InvokeScrollRightButtonClick()
		{
			scrollRightButton.SimulateClick();
			return UniTask.CompletedTask;
		}

		private void UnSelectEventSystemObject()
		{
			EventSystem.current.SetSelectedGameObject(null);
		}

		private RuntimeWeaponData[] GetAllSignatureWeapons()
		{
			WeaponData[] array = GameDirector.Instance.runtimeDB.WeaponDB.Weapons.Where((WeaponData element) => element.IsSignature && GameDirector.Instance.runtimeDB.UnlockedPoetPools.Contains(element.poolID)).ToArray();
			List<RuntimeWeaponData> list = new List<RuntimeWeaponData>();
			for (int num = 0; num < array.Length; num++)
			{
				if (array[num].IsSignature)
				{
					list.Add(new RuntimeWeaponData(array[num]));
				}
			}
			return list.ToArray();
		}
	}
}
