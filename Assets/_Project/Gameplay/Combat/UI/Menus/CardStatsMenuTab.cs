using System;
using System.Collections.Generic;
using System.Linq;
using Animancer;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Controllers;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.HellMaiden.UI.Menus.PauseMenu;
using AstralShift.Helpers;
using AstralShift.Managers;
using AstralShift.UI;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using FMODUnity;
using UnityEngine;

namespace AstralShift.HellMaiden.UI.Menus
{
	public class CardStatsMenuTab : TabContentController
	{
		private StatsMenuController _statsMenuController;

		[SerializeField]
		private CanvasGroup parentCanvasGroup;

		[SerializeField]
		private CanvasGroup canvasGroup;

		[Header("Buttons")]
		[SerializeField]
		private CustomUIButton nextWeaponSlotBt;

		[SerializeField]
		private CustomUIButton previousWeaponSlotBt;

		[SerializeField]
		private CustomUIButton nextSlotCardBt;

		[SerializeField]
		private CustomUIButton previousSlotCardBt;

		[Header("Cards")]
		[SerializeField]
		private GameObject cardsParent;

		[SerializeField]
		private UICardOrPerkStaticElement weaponCard;

		[SerializeField]
		private List<UICardOrPerkStaticElement> equipmentCards;

		[SerializeField]
		private List<UICardOrPerkStaticElement> cornerCardVisuals;

		[SerializeField]
		private CardInformationPanel cardInformationPanel;

		[SerializeField]
		private Transform cardAmountPositions;

		[Header("Sound")]
		[SerializeField]
		private EventReference cardSlidingSound;

		[Header("Animations")]
		[SerializeField]
		private ClipTransition nextSlotAnimation;

		[SerializeField]
		private ClipTransition previousSlotAnimation;

		[SerializeField]
		private ClipTransition nextSlotCardAnimation;

		[SerializeField]
		private ClipTransition previousSlotCardAnimation;

		[Header("Animation Options")]
		[SerializeField]
		private float shiftCardAnimationDuration = 0.5f;

		[SerializeField]
		private float cardBeginJumpAnimationDuration = 0.5f;

		[SerializeField]
		private float cardEndJumpAnimationDuration = 0.5f;

		[SerializeField]
		private CustomAnimationCurve cardShiftAnimationCurve;

		[SerializeField]
		private CustomAnimationCurve cardBeginJumpAnimationCurve;

		[SerializeField]
		private CustomAnimationCurve cardEndJumpAnimationCurve;

		[SerializeField]
		private RectTransform downPosition;

		[SerializeField]
		private RectTransform upPosition;

		private int _currentSlotIndex;

		private int _currentSlotCardIndex;

		private Tween _activeCardTween;

		private AnimancerState _openCloseAnimationState;

		private AnimancerState _slotTransitionAnimationState;

		private AnimancerState _animationState;

		private List<UICardOrPerkStaticElement> _allCards = new List<UICardOrPerkStaticElement>();

		private const float CardOffset = 54f;

		public override void Init()
		{
			base.Init();
			EnableSlotNavigationInteraction(state: false);
			_allCards = new List<UICardOrPerkStaticElement>();
			_allCards = equipmentCards.ToList();
			_allCards.Insert(0, weaponCard);
			RegisterOnClickEvents();
		}

		public override void Open(bool instant = false)
		{
			_statsMenuController = ControllerManager.Instance.CurrentController as StatsMenuController;
			_statsMenuController.EnableMenuInteraction(state: false);
			RegisterControllerEvents();
			base.Open(instant);
			_currentSlotCardIndex = 0;
			if (PlayerHand.Instance.WeaponCount == 0)
			{
				cardInformationPanel.ShowEmptyText();
			}
			else
			{
				SearchForActiveSlot();
				SetCornerCardsVisuals();
				SetCardsVisuals();
				OpenWeaponInfoPanel();
			}
			EnableSlotNavigationInteraction(state: false);
			if (PlayerHand.Instance.WeaponCount > 1)
			{
				EnableSlotNavigationInteraction(state: true);
			}
		}

		public override void Close(bool instant = false)
		{
			base.Close();
			UnRegisterControllerEvents();
		}

		protected override void OnOpeningFinished()
		{
			base.OnOpeningFinished();
			_statsMenuController.EnableMenuInteraction(state: true);
		}

		private void RegisterOnClickEvents()
		{
			nextWeaponSlotBt.onSubmit.RemoveAllListeners();
			previousWeaponSlotBt.onSubmit.RemoveAllListeners();
			nextSlotCardBt.onSubmit.RemoveAllListeners();
			previousSlotCardBt.onSubmit.RemoveAllListeners();
			nextWeaponSlotBt.onSubmit.AddListener(NextSlot);
			previousWeaponSlotBt.onSubmit.AddListener(PreviousSlot);
			nextSlotCardBt.onSubmit.AddListener(NextSlotCard);
			previousSlotCardBt.onSubmit.AddListener(PreviousSlotCard);
		}

		private void RegisterControllerEvents()
		{
			_statsMenuController.OnDirectionalRight += delegate
			{
				nextWeaponSlotBt.OnSubmit(null);
			};
			_statsMenuController.OnDirectionalLeft += delegate
			{
				previousWeaponSlotBt.OnSubmit(null);
			};
			_statsMenuController.OnDirectionalUp += delegate
			{
				nextSlotCardBt.OnSubmit(null);
			};
			_statsMenuController.OnDirectionalDown += delegate
			{
				previousSlotCardBt.OnSubmit(null);
			};
		}

		private void UnRegisterControllerEvents()
		{
			_statsMenuController.CleanControllerActions();
		}

		private void EnableSlotNavigationInteraction(bool state)
		{
			nextWeaponSlotBt.interactable = state;
			previousWeaponSlotBt.interactable = state;
		}

		private void EnableSlotCardNavigationInteraction(bool state)
		{
			nextSlotCardBt.interactable = state;
			previousSlotCardBt.interactable = state;
		}

		public void OpenWeaponInfoPanel()
		{
			WeaponBehaviour weaponBehaviour = PlayerHand.Instance.GetHandSlotFromIndex(_currentSlotIndex).WeaponBehaviour;
			int count = PlayerHand.Instance.GetHandSlotFromIndex(_currentSlotIndex).Equipments.Count;
			EnableSlotCardNavigationInteraction(state: true);
			if (count == 0)
			{
				EnableSlotCardNavigationInteraction(state: false);
			}
			WeaponData data = PlayerHand.Instance.GetHandSlotFromIndex(_currentSlotIndex).RuntimeWeaponData.Data;
			cardsParent.SetActive(value: true);
			cardInformationPanel.ShowWeaponStatsText(weaponBehaviour, data);
		}

		public void SetCardsVisuals()
		{
			PlayerHandSlot handSlotFromIndex = PlayerHand.Instance.GetHandSlotFromIndex(_currentSlotIndex);
			int count = handSlotFromIndex.Equipments.Count;
			weaponCard.transform.position = _allCards[0].transform.position;
			weaponCard.SetCardVisuals(handSlotFromIndex.RuntimeWeaponData);
			for (int i = 0; i < cardAmountPositions.childCount - 1; i++)
			{
				cardAmountPositions.GetChild(i).gameObject.SetActive(value: false);
			}
			_allCards.Remove(weaponCard);
			for (int j = 0; j < count; j++)
			{
				RuntimeEquipmentData runtimeEquipmentData = handSlotFromIndex.Equipments[j];
				bool isCompat = handSlotFromIndex.IsEquipmentCompatible(runtimeEquipmentData);
				_allCards[j].SetCardVisuals(runtimeEquipmentData, isCompat);
				cardAmountPositions.GetChild(cardAmountPositions.childCount - 1 - j).gameObject.SetActive(value: true);
			}
			_allCards.Insert(0, weaponCard);
			for (int k = 1; k < PlayerHand.MAX_EQUIPS_PER_SLOT + 1; k++)
			{
				_allCards[k].transform.SetSiblingIndex(_allCards.Count - k);
				_allCards[k].gameObject.SetActive(k <= count);
			}
			weaponCard.transform.SetAsLastSibling();
			StartCoroutine(Wait.SetFrameTimeout(1, delegate
			{
				cardsParent.transform.position = cardAmountPositions.GetChild(cardAmountPositions.childCount - 1).position;
			}));
			SetActiveCardVisual();
		}

		private void SetCornerCardsVisuals()
		{
			int count = PlayerHand.Instance.Slots.Count;
			for (int i = 0; i < count; i++)
			{
				cornerCardVisuals[i].SetCardVisuals(PlayerHand.Instance.GetHandSlotFromIndex(i).RuntimeWeaponData);
			}
		}

		private void SetActiveCardVisual()
		{
			for (int i = 0; i < cornerCardVisuals.Count; i++)
			{
				Vector3 position = cornerCardVisuals[i].transform.position;
				position.y = 0f;
				if (i == _currentSlotIndex)
				{
					cornerCardVisuals[i].SetColor(Color.white);
					cornerCardVisuals[i].transform.DOMove(position + Vector3.up * UIResolutionHelpers.GetResAdjustedScreenSpaceOffset(27.5f), 0.5f).SetUpdate(isIndependentUpdate: true);
				}
				else
				{
					cornerCardVisuals[i].transform.DOMove(position, 0.5f).SetUpdate(isIndependentUpdate: true);
					cornerCardVisuals[i].SetColor(Color.grey);
				}
			}
		}

		private void SearchForActiveSlot()
		{
			int count = PlayerHand.Instance.Slots.Count;
			for (int i = 0; i < count; i++)
			{
				if ((bool)PlayerHand.Instance.GetHandSlotFromIndex(i).WeaponBehaviour)
				{
					_currentSlotIndex = i;
					break;
				}
			}
		}

		private async void NextSlot()
		{
			try
			{
				if (PlayerHand.Instance.WeaponCount == 0)
				{
					cardInformationPanel.ShowEmptyText();
					return;
				}
				_statsMenuController.EnableMenuInteraction(state: false);
				_currentSlotCardIndex = 0;
				_currentSlotIndex++;
				if (_currentSlotIndex > PlayerHand.Instance.Slots.Count - 1)
				{
					_currentSlotIndex = 0;
				}
				if (!PlayerHand.Instance.GetHandSlotFromIndex(_currentSlotIndex).WeaponBehaviour)
				{
					NextSlot();
					return;
				}
				await NextSlotAnimation();
				_statsMenuController.EnableMenuInteraction(state: true);
				OpenWeaponInfoPanel();
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		private async void PreviousSlot()
		{
			try
			{
				if (PlayerHand.Instance.WeaponCount == 0)
				{
					cardInformationPanel.ShowEmptyText();
					return;
				}
				_statsMenuController.EnableMenuInteraction(state: false);
				_currentSlotCardIndex = 0;
				_currentSlotIndex--;
				if (_currentSlotIndex < 0)
				{
					_currentSlotIndex = PlayerHand.Instance.Slots.Count - 1;
				}
				if (!PlayerHand.Instance.GetHandSlotFromIndex(_currentSlotIndex).WeaponBehaviour)
				{
					PreviousSlot();
					return;
				}
				await PreviousSlotAnimation();
				_statsMenuController.EnableMenuInteraction(state: true);
				OpenWeaponInfoPanel();
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		private async void NextSlotCard()
		{
			_ = 1;
			try
			{
				if (PlayerHand.Instance.GetHandSlotFromIndex(_currentSlotIndex).Equipments.Count != 0)
				{
					_statsMenuController.EnableMenuInteraction(state: false);
					_currentSlotCardIndex++;
					int count = PlayerHand.Instance.GetHandSlotFromIndex(_currentSlotIndex).Equipments.Count;
					if (_currentSlotCardIndex > count)
					{
						_currentSlotCardIndex = 0;
						OpenWeaponInfoPanel();
						await UniTask.WhenAll(ShiftCardsUp(), NextSlotCardAnimation());
						_statsMenuController.EnableMenuInteraction(state: true);
					}
					else
					{
						WeaponBehaviour weaponBehaviour = PlayerHand.Instance.GetHandSlotFromIndex(_currentSlotIndex).WeaponBehaviour;
						WeaponData data = PlayerHand.Instance.GetHandSlotFromIndex(_currentSlotIndex).RuntimeWeaponData.Data;
						RuntimeEquipmentData equipment = PlayerHand.Instance.GetHandSlotFromIndex(_currentSlotIndex).Equipments[_currentSlotCardIndex - 1];
						cardInformationPanel.ShowEquipmentStatsText(weaponBehaviour, data, equipment);
						await UniTask.WhenAll(ShiftCardsUp(), NextSlotCardAnimation());
						_statsMenuController.EnableMenuInteraction(state: true);
					}
				}
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		private async void PreviousSlotCard()
		{
			_ = 1;
			try
			{
				if (PlayerHand.Instance.GetHandSlotFromIndex(_currentSlotIndex).Equipments.Count == 0)
				{
					return;
				}
				_statsMenuController.EnableMenuInteraction(state: false);
				_currentSlotCardIndex--;
				if (_currentSlotCardIndex == 0)
				{
					OpenWeaponInfoPanel();
					await UniTask.WhenAll(ShiftCardsDown(), PreviousSlotCardAnimation());
					_statsMenuController.EnableMenuInteraction(state: true);
					return;
				}
				if (_currentSlotCardIndex < 0)
				{
					_currentSlotCardIndex = PlayerHand.Instance.GetHandSlotFromIndex(_currentSlotIndex).Equipments.Count;
				}
				WeaponBehaviour weaponBehaviour = PlayerHand.Instance.GetHandSlotFromIndex(_currentSlotIndex).WeaponBehaviour;
				WeaponData data = PlayerHand.Instance.GetHandSlotFromIndex(_currentSlotIndex).RuntimeWeaponData.Data;
				RuntimeEquipmentData equipment = PlayerHand.Instance.GetHandSlotFromIndex(_currentSlotIndex).Equipments[_currentSlotCardIndex - 1];
				cardInformationPanel.ShowEquipmentStatsText(weaponBehaviour, data, equipment);
				await UniTask.WhenAll(ShiftCardsDown(), PreviousSlotCardAnimation());
				_statsMenuController.EnableMenuInteraction(state: true);
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		private async UniTask NextSlotAnimation()
		{
			menuAnimator.Layers[1].Stop();
			await AnimancerHelpers.AnimationTask(menuAnimator, nextSlotAnimation, 1);
		}

		private async UniTask PreviousSlotAnimation()
		{
			menuAnimator.Layers[1].Stop();
			await AnimancerHelpers.AnimationTask(menuAnimator, previousSlotAnimation, 1);
		}

		private async UniTask NextSlotCardAnimation()
		{
			menuAnimator.Layers[1].Stop();
			await AnimancerHelpers.AnimationTask(menuAnimator, nextSlotCardAnimation, 1);
		}

		private async UniTask PreviousSlotCardAnimation()
		{
			menuAnimator.Layers[1].Stop();
			await AnimancerHelpers.AnimationTask(menuAnimator, previousSlotCardAnimation, 1);
		}

		private async UniTask ShiftCardsUp()
		{
			RuntimeManager.PlayOneShot(cardSlidingSound);
			int cardCount = PlayerHand.Instance.GetHandSlotFromIndex(_currentSlotIndex).Equipments.Count + 1;
			Vector3 position = _allCards[cardCount - 1].transform.position;
			if (_allCards[cardCount - 1] == weaponCard)
			{
				position = _allCards[cardCount - 2].transform.position;
			}
			if (_allCards[0] == weaponCard)
			{
				position += Vector3.up * UIResolutionHelpers.GetResAdjustedScreenSpaceOffset(54f);
			}
			Tween t = _allCards[0].transform.DOMove(upPosition.transform.position, cardBeginJumpAnimationDuration).SetUpdate(UpdateType.Late, isIndependentUpdate: true).SetEase(cardBeginJumpAnimationCurve.GetEaseFunction());
			if (_allCards[0] != weaponCard)
			{
				for (int i = 1; i < cardCount; i++)
				{
					Vector3 endValue = _allCards[i].transform.position - Vector3.up * UIResolutionHelpers.GetResAdjustedScreenSpaceOffset(54f);
					_allCards[i].transform.DOMove(endValue, shiftCardAnimationDuration).SetUpdate(UpdateType.Late, isIndependentUpdate: true).SetEase(cardShiftAnimationCurve.GetEaseFunction());
				}
			}
			await t.AsyncWaitForCompletion();
			UICardOrPerkStaticElement uICardOrPerkStaticElement = _allCards[0];
			t = uICardOrPerkStaticElement.transform.DOMove(position, cardEndJumpAnimationDuration).SetUpdate(isIndependentUpdate: true).SetEase(cardEndJumpAnimationCurve.GetEaseFunction());
			uICardOrPerkStaticElement.transform.SetSiblingIndex(0);
			_allCards.RemoveAt(0);
			_allCards.Insert(cardCount - 1, uICardOrPerkStaticElement);
			await t.AsyncWaitForCompletion();
		}

		private async UniTask ShiftCardsDown()
		{
			RuntimeManager.PlayOneShot(cardSlidingSound);
			int cardCount = PlayerHand.Instance.GetHandSlotFromIndex(_currentSlotIndex).Equipments.Count + 1;
			Vector3 position = _allCards[0].transform.position;
			Tween t = _allCards[cardCount - 1].transform.DOMove(downPosition.transform.position, cardBeginJumpAnimationDuration).SetUpdate(UpdateType.Late, isIndependentUpdate: true).SetEase(cardBeginJumpAnimationCurve.GetEaseFunction());
			if (_allCards[cardCount - 1] != weaponCard)
			{
				for (int i = 0; i < cardCount - 1; i++)
				{
					Vector3 endValue = _allCards[i].transform.position + Vector3.up * UIResolutionHelpers.GetResAdjustedScreenSpaceOffset(54f);
					_allCards[i].transform.DOMove(endValue, shiftCardAnimationDuration).SetUpdate(UpdateType.Late, isIndependentUpdate: true).SetEase(cardShiftAnimationCurve.GetEaseFunction());
				}
			}
			await t.AsyncWaitForCompletion();
			UICardOrPerkStaticElement uICardOrPerkStaticElement = _allCards[cardCount - 1];
			t = uICardOrPerkStaticElement.transform.DOMove(position, cardEndJumpAnimationDuration).SetUpdate(isIndependentUpdate: true).SetEase(cardEndJumpAnimationCurve.GetEaseFunction());
			uICardOrPerkStaticElement.transform.SetSiblingIndex(_allCards.Count);
			_allCards.RemoveAt(cardCount - 1);
			_allCards.Insert(0, uICardOrPerkStaticElement);
			await t.AsyncWaitForCompletion();
		}
	}
}
