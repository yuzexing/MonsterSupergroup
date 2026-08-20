using System;
using System.Collections.Generic;
using System.Linq;
using AstralShift.Control;
using AstralShift.FSM;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.HellMaiden.UI.Cards;
using AstralShift.Helpers;
using Coffee.UIExtensions;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using FMODUnity;
using Rewired;
using Sirenix.Utilities;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI.Menus.Hand
{
	public class PlayerHandSlotView : MonoBehaviour
	{
		private PlayerHandView _handView;

		private PlayerHandSlot _playerHandSlot;

		[SerializeField]
		private EquipmentSlotView equipmentSlotViewPrefab;

		[SerializeField]
		private CardAnimationSettings _animationSettings;

		[Space]
		[Header("Views Containers / Pivots")]
		[SerializeField]
		private LayoutGroup layoutGroup;

		[SerializeField]
		private RectTransform _slotViewsContainer;

		private LayoutElement _slotViewsContainerLayoutElement;

		[SerializeField]
		private WeaponSlotView weaponSlotView;

		[SerializeField]
		private UICardViewContainer weaponViewContainer;

		[SerializeField]
		private UICardViewContainer equipmentViewContainer;

		[SerializeField]
		private UICardViewContainer equipmentSlotViewContainer;

		[SerializeField]
		private UICardViewContainer backEffectsSlotViewContainer;

		private LinkedList<EquipmentSlotView> _equipmentSlots;

		private EquipmentSlotView _availableEquipmentSlotView;

		[Space]
		[SerializeField]
		protected Transform equipmentsVerticalLayout;

		[SerializeField]
		private Image raycastTarget;

		[SerializeField]
		private CanvasGroup canvasGroup;

		[SerializeField]
		private ViewFollower bgGlowEffect;

		[SerializeField]
		protected Image selectionIndicator;

		[Header("Variables")]
		[SerializeField]
		private float raycastTargetExpansionSize;

		public Transform parentTransform;

		[Header("Hand Slot Folding Variables")]
		[FormerlySerializedAs("scaleEase")]
		public CustomAnimationCurve foldScaleEase;

		private Coroutine _foldScaleCoroutine;

		[SerializeField]
		private float normalScale = 1f;

		[SerializeField]
		private float scaleDownSize = 0.8f;

		[SerializeField]
		private int scaleDownLeftPadding;

		[SerializeField]
		private int scaleDownRightPadding;

		[SerializeField]
		private int scaleDownBottomPadding;

		[SerializeField]
		private float scaleUpSize = 1.3f;

		[SerializeField]
		private int scaleUpLeftPadding = 30;

		[SerializeField]
		private int scaleUpRightPadding = 40;

		[SerializeField]
		private int scaleUpBottomPadding = -25;

		[SerializeField]
		private float scaleNormalDuration = 0.3f;

		[SerializeField]
		private float scaleDownDuration = 0.3f;

		[SerializeField]
		private float scaleUpDuration = 0.3f;

		private List<UIEquipmentCardViewHandler> _navigationTempList;

		private StateMachine _selectionStateMachine;

		private State _idle;

		private State _dragging;

		private StateMachine _foldStateMachine;

		private State _folded;

		private State _unFolded;

		[SerializeField]
		protected PlayerHandSlotViewMouseHandler _mouseHandler;

		[SerializeField]
		protected PlayerHandSlotViewGamepadHandler _gamepadHandler;

		[Header("Sounds")]
		[SerializeField]
		private EventReference mergeSound;

		[SerializeField]
		private EventReference equipSound;

		private const int CloseMenuTimeoutInMs = 250;

		private UICardViewHandler _magnetLockedCard;

		private Tween _scaleTween;

		private Sequence _mergeAnimationTweenSequence;

		private UIParticle _mergeGodRaysParticleSystem;

		private Sequence _slotSelectionArrowTween;

		private Sequence _slotSelectionMoveTween;

		public PlayerHandView HandView => _handView;

		public PlayerHandSlot HandSlot => _playerHandSlot;

		public LayoutGroup LayoutGroup => layoutGroup;

		public RectTransform SlotViewsContainer => _slotViewsContainer;

		private LayoutElement SlotViewsContainerLayoutElement
		{
			get
			{
				if (!_slotViewsContainerLayoutElement)
				{
					_slotViewsContainerLayoutElement = _slotViewsContainer.GetComponent<LayoutElement>();
				}
				return _slotViewsContainerLayoutElement;
			}
		}

		public WeaponSlotView WeaponSlotView => weaponSlotView;

		public UICardViewContainer WeaponViewContainer => weaponViewContainer;

		public UICardViewContainer EquipmentViewContainer => equipmentViewContainer;

		public UICardViewContainer EquipmentSlotViewContainer => equipmentSlotViewContainer;

		public UICardViewContainer BackEffectsSlotViewContainer => backEffectsSlotViewContainer;

		public LinkedList<EquipmentSlotView> EquipmentSlots => _equipmentSlots;

		public bool IsIdle
		{
			get
			{
				if (_selectionStateMachine != null)
				{
					return _selectionStateMachine.GetState() == _idle;
				}
				return false;
			}
		}

		public bool IsDragging
		{
			get
			{
				if (_selectionStateMachine != null)
				{
					return _selectionStateMachine.GetState() == _dragging;
				}
				return false;
			}
		}

		public bool IsFolded
		{
			get
			{
				if (_foldStateMachine != null)
				{
					return _foldStateMachine.GetState() == _folded;
				}
				return false;
			}
		}

		public bool IsUnFolded
		{
			get
			{
				if (_foldStateMachine != null)
				{
					return _foldStateMachine.GetState() == _unFolded;
				}
				return false;
			}
		}

		public PlayerHandSlotViewMouseHandler MouseHandler => _mouseHandler;

		public PlayerHandSlotViewGamepadHandler GamepadHandler => _gamepadHandler;

		public bool IsBusy { get; private set; }

		public event Action HideAllCompatVFX;

		public void Init(PlayerHandView handView, PlayerHandSlot slot)
		{
			_handView = handView;
			_playerHandSlot = slot;
			ControllerLifetime.OnBeforeControllerChanged += SwitchInputHandler;
			weaponSlotView.AssignHandSlot(this, 0);
			InstantiateEquipmentSlots();
			InitializeSelectionStateMachine();
			InitializeFoldStateMachine();
			InitializeHandSlotVFX();
		}

		private void InitializeHandSlotVFX()
		{
			bgGlowEffect.AssignParent(BackEffectsSlotViewContainer.Transform);
			ShowSelectionVFX(state: false);
		}

		public async UniTask InstantiatePreEquippedCards()
		{
			if (_playerHandSlot.RuntimeWeaponData != null)
			{
				await TryInstantiateWeaponView(_playerHandSlot.RuntimeWeaponData);
			}
			foreach (RuntimeEquipmentData equipment in _playerHandSlot.Equipments)
			{
				await TryInstantiateEquipmentView(equipment);
			}
			_playerHandSlot.ReApplyMultiSlotModifiers();
		}

		private void InstantiateEquipmentSlots()
		{
			_equipmentSlots = new LinkedList<EquipmentSlotView>();
			for (int i = 0; i < PlayerHand.MAX_EQUIPS_PER_SLOT; i++)
			{
				EquipmentSlotView equipmentSlotView = UnityEngine.Object.Instantiate(equipmentSlotViewPrefab, equipmentsVerticalLayout);
				equipmentSlotView.gameObject.name = "Equipment_Slot_" + i;
				equipmentSlotView.AssignHandSlot(this, i);
				_equipmentSlots.AddFirst(new LinkedListNode<EquipmentSlotView>(equipmentSlotView));
			}
		}

		private async UniTask<bool> TryInstantiateWeaponView(RuntimeWeaponData data)
		{
			if (!weaponSlotView.IsEmpty)
			{
				return false;
			}
			UIWeaponCardViewHandler uIWeaponCardViewHandler = (await CardVisualsFactory.GetUIWeaponCard(data)) as UIWeaponCardViewHandler;
			if (!uIWeaponCardViewHandler)
			{
				return false;
			}
			uIWeaponCardViewHandler.gameObject.AddComponent<CPMCardViewInputHandler>();
			uIWeaponCardViewHandler.Show();
			uIWeaponCardViewHandler.TransitionToDragging();
			uIWeaponCardViewHandler.AssignCardSlot(weaponSlotView);
			uIWeaponCardViewHandler.TransitionToDropped();
			HideAllCompatVFX += uIWeaponCardViewHandler.HideCompatVFX;
			WeaponViewContainer.AddWeaponCard(uIWeaponCardViewHandler);
			PlayerHand.Instance.RegisterWeaponChanges();
			return true;
		}

		private async UniTask<bool> TryInstantiateEquipmentView(RuntimeEquipmentData data)
		{
			EquipmentSlotView equipmentSlotView = GetFirstEmptyEquipmentSlot();
			if (!equipmentSlotView)
			{
				return false;
			}
			UIEquipmentCardViewHandler uIEquipmentCardViewHandler = (await CardVisualsFactory.GetUIEquipmentCard(data)) as UIEquipmentCardViewHandler;
			if (!uIEquipmentCardViewHandler)
			{
				return false;
			}
			uIEquipmentCardViewHandler.gameObject.AddComponent<CPMCardViewInputHandler>();
			uIEquipmentCardViewHandler.Show();
			uIEquipmentCardViewHandler.TransitionToDragging();
			uIEquipmentCardViewHandler.AssignCardSlot(equipmentSlotView);
			uIEquipmentCardViewHandler.TransitionToDropped();
			HideAllCompatVFX += uIEquipmentCardViewHandler.HideCompatVFX;
			EquipmentViewContainer.AddEquipmentCard(uIEquipmentCardViewHandler);
			PlayerHand.Instance.RegisterWeaponChanges();
			return true;
		}

		public void OnDestroy()
		{
			ControllerLifetime.OnBeforeControllerChanged -= SwitchInputHandler;
			this.HideAllCompatVFX = null;
		}

		private void SwitchInputHandler(ControllerType controllerType)
		{
			if (controllerType != ControllerType.Mouse)
			{
				MouseHandler.enabled = false;
				GamepadHandler.enabled = true;
			}
			else
			{
				GamepadHandler.enabled = false;
				MouseHandler.enabled = true;
			}
		}

		private void AllowInteraction(bool state)
		{
			canvasGroup.interactable = state;
		}

		public void ConstructEquipmentsNavigation()
		{
			if (!HandSlot.HasEquipments() || HandSlot.Equipments.Count == 1)
			{
				return;
			}
			if (_navigationTempList == null)
			{
				_navigationTempList = new List<UIEquipmentCardViewHandler>();
			}
			foreach (UIEquipmentCardViewHandler navigationTemp in _navigationTempList)
			{
				if ((bool)navigationTemp && !(navigationTemp.InputHandler == null))
				{
					navigationTemp.InputHandler.ClearNavigation();
				}
			}
			_navigationTempList.Clear();
			for (int i = 0; i < EquipmentSlots.Count; i++)
			{
				EquipmentSlotView equipmentSlotView = EquipmentSlots.ElementAt(i);
				if (!equipmentSlotView.IsEmpty)
				{
					_navigationTempList.Add(equipmentSlotView.CardViewHandler);
				}
			}
			for (int j = 1; j < _navigationTempList.Count; j++)
			{
				_navigationTempList[j].InputHandler.SetLeftNavigation(_navigationTempList[j - 1].InputHandler);
				_navigationTempList[j - 1].InputHandler.SetRightNavigation(_navigationTempList[j].InputHandler);
			}
		}

		public void HideSortDeckSelectionVFX()
		{
			ShowSelectionVFX(state: false);
		}

		private void InitializeSelectionStateMachine()
		{
			_selectionStateMachine = new StateMachine(base.gameObject.name + base.transform.GetSiblingIndex() + " Selection State");
			_idle = new State("Idle");
			_dragging = new State("Dragging");
			_selectionStateMachine.AddTransition(_idle, _dragging);
			_selectionStateMachine.AddTransition(_dragging, _idle);
			_selectionStateMachine.SetInitialState(_idle);
		}

		public void TransitionToIdle()
		{
			_selectionStateMachine.MakeTransition(_idle);
		}

		public void TransitionToDragging()
		{
			_selectionStateMachine.MakeTransition(_dragging);
		}

		private void InitializeFoldStateMachine()
		{
			_foldStateMachine = new StateMachine(base.gameObject.name + base.transform.GetSiblingIndex() + " Fold State");
			_folded = new State("Folded");
			_unFolded = new State("UnFolded");
			_foldStateMachine.AddTransition(_idle, _folded);
			_foldStateMachine.AddTransition(_idle, _unFolded);
			_foldStateMachine.AddTransition(_folded, _unFolded);
			_foldStateMachine.AddTransition(_unFolded, _folded);
			_foldStateMachine.SetInitialStateNoCallbacks(_idle);
		}

		public void TransitionToFold()
		{
			_foldStateMachine.MakeTransition(_folded);
		}

		public void TransitionToUnFold()
		{
			_foldStateMachine.MakeTransition(_unFolded);
		}

		public async UniTask DropWeapon(UIWeaponCardViewHandler viewHandler)
		{
			UICardPickMenuView.Instance.PickCard(viewHandler);
			viewHandler.InputHandler.ClearNavigation();
			viewHandler.Equip(weaponSlotView);
			viewHandler.AllowInteraction(value: false);
			RuntimeManager.PlayOneShot(equipSound);
			await viewHandler.CardView.EquipEffect();
			viewHandler.CardView.EnableMovement();
			HideAllCompatVFX += viewHandler.HideCompatVFX;
			UICardPickMenuView.Instance.Close(250);
		}

		public void AddWeapon(UIWeaponCardViewHandler weaponCardViewHandler)
		{
			HandSlot.AddWeapon(weaponCardViewHandler.RuntimeWeaponData);
			WeaponViewContainer.AddWeaponCard(weaponCardViewHandler);
		}

		public async UniTask TryDropCardOnSlot(UICardViewHandler cardView)
		{
			if (!cardView)
			{
				return;
			}
			HandView.TryHideMergeBubble();
			HandView.HideCompatibilityVFX();
			if (HandSlot.HasWeapon())
			{
				if (cardView is UIEquipmentCardViewHandler toDropEquipmentViewHandler)
				{
					await TryDropEquipmentOnSlot(toDropEquipmentViewHandler);
				}
			}
			else if (cardView is UIWeaponCardViewHandler viewHandler)
			{
				await DropWeapon(viewHandler);
			}
		}

		public async UniTask TryDropEquipmentOnSlot(UIEquipmentCardViewHandler toDropEquipmentViewHandler)
		{
			EquipmentSlotView availableEquipmentSlotView = _availableEquipmentSlotView;
			if (!(availableEquipmentSlotView == null))
			{
				_availableEquipmentSlotView = null;
				await TryDropEquipmentOnSlot(toDropEquipmentViewHandler, availableEquipmentSlotView);
			}
		}

		public async UniTask TryDropEquipmentOnSlot(UIEquipmentCardViewHandler toDropEquipmentViewHandler, EquipmentSlotView toDropSlotView)
		{
			IsBusy = true;
			try
			{
				if (toDropSlotView == null)
				{
					return;
				}
				bool isDroppingToHand = !toDropEquipmentViewHandler.HasBeenDropped;
				if (isDroppingToHand)
				{
					UICardPickMenuView.Instance.PickCard(toDropEquipmentViewHandler);
				}
				if (ContainsEquipmentCard(toDropEquipmentViewHandler))
				{
					ReturnEquipmentToSlot(toDropEquipmentViewHandler, toDropSlotView);
					UICardPickMenuView.Instance.Controller.TransitionToWaitingPick();
					UICardPickMenuView.Instance.EnableMenuInteraction(state: true);
					return;
				}
				UICardPickMenuView.Instance.EnableMenuInteraction(state: false);
				int mergeCount = HandSlot.GetPotentialMergeCount(toDropEquipmentViewHandler.RuntimeEquipmentData);
				if (mergeCount == 0)
				{
					DropEquipmentOnSlot(toDropEquipmentViewHandler, toDropSlotView);
					toDropEquipmentViewHandler.AllowInteraction(value: false);
					RuntimeManager.PlayOneShot(equipSound);
					await toDropEquipmentViewHandler.CardView.EquipEffect();
					toDropEquipmentViewHandler.AllowInteraction(value: true);
					toDropEquipmentViewHandler.CardView.EnableMovement();
					if (isDroppingToHand)
					{
						UICardPickMenuView.Instance.Close(250);
						return;
					}
					UICardPickMenuView.Instance.Controller.TransitionToWaitingPick();
					UICardPickMenuView.Instance.EnableMenuInteraction(state: true);
					return;
				}
				UICardPickMenuView.Instance.Controller.TransitionToMergingCards();
				bool firstMerge = true;
				while (mergeCount > 0)
				{
					EquipmentSlotView equipmentSlotToMerge = GetEquipmentSlotToMerge(toDropEquipmentViewHandler);
					if (equipmentSlotToMerge == null)
					{
						break;
					}
					Leveler.Instance.CardPool.UnRegisterChosenCard(toDropEquipmentViewHandler.RuntimeCardData);
					(UIEquipmentCardViewHandler, EquipmentSlotView) tuple = await MergeEquipment(toDropEquipmentViewHandler, equipmentSlotToMerge.CardViewHandler, firstMerge);
					firstMerge = false;
					(toDropEquipmentViewHandler, toDropSlotView) = tuple;
					if (toDropEquipmentViewHandler.RuntimeEquipmentData.IsMaxLevel())
					{
						Leveler.Instance.CardPool.TryExcludeMaxLevelEquipment(toDropEquipmentViewHandler.RuntimeEquipmentData);
					}
					DropEquipmentOnSlot(toDropEquipmentViewHandler, toDropSlotView, sort: false, rebuildNavigation: false);
					mergeCount--;
				}
				await MergeEndAnimation(toDropEquipmentViewHandler, toDropSlotView.SlotContainer.position);
				toDropEquipmentViewHandler.LockMotion(state: false);
				await toDropEquipmentViewHandler.CardView.EquipEffectOnMerge();
				toDropEquipmentViewHandler.CardView.SetRenderOrderToDefault();
				toDropEquipmentViewHandler.CardView.EnableMovement();
				toDropEquipmentViewHandler.AllowInteraction(value: true);
				toDropEquipmentViewHandler.CardView.EnableStaticRender(state: true);
				SortEquipmentSlots();
				HandView.ConstructHandNavigation();
				if (isDroppingToHand)
				{
					UICardPickMenuView.Instance.Close(250);
					return;
				}
				UICardPickMenuView.Instance.Controller.TransitionToWaitingPick();
				UICardPickMenuView.Instance.EnableMenuInteraction(state: true);
			}
			finally
			{
				IsBusy = false;
			}
		}

		private void DropEquipmentOnSlot(UIEquipmentCardViewHandler equipmentViewHandler, CardSlotView slotView, bool sort = true, bool rebuildNavigation = true)
		{
			HandView.ClearHandSlotsNavigation();
			equipmentViewHandler.InputHandler.ClearNavigation();
			equipmentViewHandler.UnEquip();
			equipmentViewHandler.Equip(slotView);
			if (sort)
			{
				SortEquipmentSlots();
			}
			if (!IsEquipmentCompatible(equipmentViewHandler))
			{
				equipmentViewHandler.CardView.ShowUnCompatVFX(state: true);
			}
			else
			{
				equipmentViewHandler.CardView.ShowUnCompatVFX(state: false);
			}
			if (rebuildNavigation)
			{
				HandView.ConstructHandNavigation();
			}
			HideAllCompatVFX += equipmentViewHandler.HideCompatVFX;
		}

		private void ReturnEquipmentToSlot(UIEquipmentCardViewHandler equipmentViewHandler, CardSlotView slotView, bool sort = true)
		{
			HandView.ClearHandSlotsNavigation();
			equipmentViewHandler.InputHandler.ClearNavigation();
			equipmentViewHandler.DropInSlot(slotView);
			if (sort)
			{
				SortEquipmentSlots();
			}
			if (!IsEquipmentCompatible(equipmentViewHandler))
			{
				equipmentViewHandler.CardView.ShowUnCompatVFX(state: true);
			}
			else
			{
				equipmentViewHandler.CardView.ShowUnCompatVFX(state: false);
			}
			HandView.ConstructHandNavigation();
			HideAllCompatVFX += equipmentViewHandler.HideCompatVFX;
		}

		public void AddEquipment(UIEquipmentCardViewHandler equipmentCardViewHandler)
		{
			HandSlot.AddEquipment(equipmentCardViewHandler.RuntimeEquipmentData);
			EquipmentViewContainer.AddEquipmentCard(equipmentCardViewHandler);
		}

		public void ReturnEquipment(UIEquipmentCardViewHandler equipmentCardViewHandler)
		{
			EquipmentViewContainer.AddEquipmentCard(equipmentCardViewHandler);
		}

		public void RemoveEquipment(UIEquipmentCardViewHandler equipmentCardViewHandler)
		{
			HandSlot.RemoveEquipment(equipmentCardViewHandler.RuntimeEquipmentData);
		}

		private async UniTask<(UIEquipmentCardViewHandler, EquipmentSlotView)> MergeEquipment(UIEquipmentCardViewHandler toDropEquipment, UIEquipmentCardViewHandler originalEquipment, bool firstMerge = true)
		{
			EquipmentSlotView slotView = originalEquipment.SlotView;
			originalEquipment.InputHandler.ClearBindings();
			toDropEquipment.InputHandler.ClearBindings();
			originalEquipment.InputHandler.ClearNavigation();
			toDropEquipment.InputHandler.ClearNavigation();
			if (originalEquipment.CardSlot == toDropEquipment.CardSlot)
			{
				originalEquipment.UnEquip();
				toDropEquipment.UnEquip();
			}
			else
			{
				originalEquipment.UnEquip(sort: false);
				toDropEquipment.UnEquip();
			}
			originalEquipment.AllowInteraction(value: false);
			toDropEquipment.AllowInteraction(value: false);
			originalEquipment.CardView.EnableStaticRender(state: false);
			toDropEquipment.CardView.EnableStaticRender(state: false);
			originalEquipment.CardView.SetRenderOrderTopMost();
			toDropEquipment.CardView.SetRenderOrderTopMost();
			await MergeStartAnimation(originalEquipment, toDropEquipment, slotView, firstMerge);
			originalEquipment.RuntimeEquipmentData.IncreaseLevel();
			await CardVisualsFactory.RefreshUIEquipmentCard(originalEquipment);
			UnityEngine.Object.Destroy(toDropEquipment.gameObject);
			await MergeRevealAnimation(originalEquipment);
			return (originalEquipment, slotView);
		}

		public bool ContainsEquipmentCard(UIEquipmentCardViewHandler equipmentViewHandler)
		{
			for (int i = 0; i < _equipmentSlots.Count; i++)
			{
				if (_equipmentSlots.ElementAt(i).CardViewHandler == equipmentViewHandler)
				{
					return true;
				}
			}
			return false;
		}

		public bool ContainsEquipmentCard(UIEquipmentCardViewHandler equipmentViewHandler, out EquipmentSlotView result)
		{
			for (int i = 0; i < EquipmentSlots.Count; i++)
			{
				if (EquipmentSlots.ElementAt(i).CardViewHandler == equipmentViewHandler)
				{
					result = EquipmentSlots.ElementAt(i);
					return true;
				}
			}
			result = null;
			return false;
		}

		public EquipmentSlotView GetEquipmentSlotToMerge(UIEquipmentCardViewHandler equipmentViewHandler)
		{
			for (int i = 0; i < EquipmentSlots.Count; i++)
			{
				EquipmentSlotView equipmentSlotView = EquipmentSlots.ElementAt(i);
				if (!(equipmentViewHandler == equipmentSlotView.CardViewHandler) && !equipmentSlotView.IsEmpty)
				{
					UIEquipmentCardViewHandler cardViewHandler = equipmentSlotView.CardViewHandler;
					if (RuntimeEquipmentData.CanMerge(equipmentViewHandler.RuntimeEquipmentData, cardViewHandler.RuntimeEquipmentData))
					{
						return equipmentSlotView;
					}
				}
			}
			return null;
		}

		public EquipmentSlotView GetFirstEmptyEquipmentSlot(UIEquipmentCardViewHandler equipmentViewHandler)
		{
			for (int i = 0; i < EquipmentSlots.Count; i++)
			{
				EquipmentSlotView equipmentSlotView = EquipmentSlots.ElementAt(i);
				if (!(equipmentSlotView.CardViewHandler == equipmentViewHandler) && equipmentSlotView.IsEmpty)
				{
					return equipmentSlotView;
				}
			}
			return null;
		}

		public EquipmentSlotView GetFirstEmptyEquipmentSlot()
		{
			for (int i = 0; i < EquipmentSlots.Count; i++)
			{
				EquipmentSlotView equipmentSlotView = EquipmentSlots.ElementAt(i);
				if (equipmentSlotView.IsEmpty)
				{
					return equipmentSlotView;
				}
			}
			return null;
		}

		public UIEquipmentCardViewHandler GetFirstFoundEquipmentView()
		{
			if (!HandSlot.HasEquipments())
			{
				return null;
			}
			for (int i = 0; i < EquipmentSlots.Count; i++)
			{
				EquipmentSlotView equipmentSlotView = EquipmentSlots.ElementAt(i);
				if (!equipmentSlotView.IsEmpty)
				{
					return equipmentSlotView.CardViewHandler;
				}
			}
			return null;
		}

		public UIEquipmentCardViewHandler GetLastFoundEquipmentView()
		{
			if (HandSlot.Equipments.Count == 0)
			{
				return null;
			}
			for (int num = EquipmentSlots.Count - 1; num >= 0; num--)
			{
				EquipmentSlotView equipmentSlotView = EquipmentSlots.ElementAt(num);
				if (!equipmentSlotView.IsEmpty)
				{
					return equipmentSlotView.CardViewHandler;
				}
			}
			return null;
		}

		public void SetAvailableEquipmentSlotView(EquipmentSlotView slotView)
		{
			_availableEquipmentSlotView = slotView;
		}

		public async UniTask<bool> TryMagnetLock(UICardViewHandler cardView, bool ignoreAssignedSlot = true)
		{
			IsBusy = true;
			try
			{
				if (!HandSlot.HasWeapon() && cardView is UIWeaponCardViewHandler uIWeaponCardViewHandler)
				{
					weaponSlotView.StartMagnetContainerAnimation();
					await uIWeaponCardViewHandler.CardView.MagnetLock(WeaponSlotView.MagnetContainer, Vector3.zero);
					return true;
				}
				if (HandSlot.HasWeapon() && cardView is UIEquipmentCardViewHandler uIEquipmentCardViewHandler)
				{
					if (ContainsEquipmentCard(uIEquipmentCardViewHandler, out var result))
					{
						if (ignoreAssignedSlot)
						{
							return false;
						}
						SetAvailableEquipmentSlotView(result);
						HandView.UnfoldHand(this);
						result.StartMagnetContainerAnimation();
						uIEquipmentCardViewHandler.CardView.ShowMergeMagnetLockVFX(state: false);
						await uIEquipmentCardViewHandler.CardView.MagnetLock(result.MagnetContainer, Vector3.zero);
						return true;
					}
					EquipmentSlotView equipmentSlotToMerge = GetEquipmentSlotToMerge(uIEquipmentCardViewHandler);
					SetAvailableEquipmentSlotView(equipmentSlotToMerge);
					if (equipmentSlotToMerge != null)
					{
						HandView.UnfoldHand(this);
						await ApplyMergeMagnetLock(equipmentSlotToMerge, uIEquipmentCardViewHandler);
						return true;
					}
					equipmentSlotToMerge = GetFirstEmptyEquipmentSlot(uIEquipmentCardViewHandler);
					SetAvailableEquipmentSlotView(equipmentSlotToMerge);
					if (equipmentSlotToMerge == null)
					{
						return false;
					}
					HandView.UnfoldHand(this);
					HandView.TryHideMergeBubble();
					DisableMergeMagnetLockGlow();
					equipmentSlotToMerge.StartMagnetContainerAnimation();
					await uIEquipmentCardViewHandler.CardView.MagnetLock(equipmentSlotToMerge.MagnetContainer, Vector3.zero);
					return true;
				}
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
			finally
			{
				IsBusy = false;
			}
			return false;
		}

		public bool CanMagnetLock(UICardViewHandler cardView, bool ignoreAssignedSlot = true)
		{
			if (!cardView)
			{
				return false;
			}
			if (!HandSlot.HasWeapon() && cardView.TryGetComponent<UIWeaponCardViewHandler>(out var _))
			{
				return true;
			}
			if (HandSlot.HasWeapon() && cardView.TryGetComponent<UIEquipmentCardViewHandler>(out var component2))
			{
				if (ContainsEquipmentCard(component2))
				{
					return !ignoreAssignedSlot;
				}
				if (GetEquipmentSlotToMerge(component2) != null)
				{
					return true;
				}
				return GetFirstEmptyEquipmentSlot(component2) != null;
			}
			return false;
		}

		public void TryStopMagnetLock(UICardViewHandler cardView)
		{
			if ((bool)cardView)
			{
				IsBusy = false;
				StopMagnetContainerAnimations();
				cardView.CardView.KillMagnetLock();
				HandView.TryHideMergeBubble();
				cardView.CardView.ShowMergeMagnetLockVFX(state: false);
				DisableMergeMagnetLockGlow();
			}
		}

		private async UniTask ApplyMergeMagnetLock(EquipmentSlotView targetSlotView, UIEquipmentCardViewHandler equipmentViewHandler)
		{
			if (!(equipmentViewHandler == null) && !(equipmentViewHandler.CardView == null))
			{
				UniTask uniTask = equipmentViewHandler.CardView.MagnetLock(targetSlotView.MergePivot, Vector3.zero);
				_handView.ShowMergeBubble(targetSlotView.MergeBubblePivot);
				equipmentViewHandler.CardView.ShowMergeMagnetLockVFX(state: true);
				if ((bool)targetSlotView.CardViewHandler)
				{
					targetSlotView.CardViewHandler.CardView.ShowMergeMagnetLockVFX(state: true);
				}
				await uniTask;
			}
		}

		public async void SortEquipmentSlots()
		{
			List<EquipmentSlotView> list = new List<EquipmentSlotView>();
			List<EquipmentSlotView> list2 = new List<EquipmentSlotView>();
			foreach (EquipmentSlotView equipmentSlot in EquipmentSlots)
			{
				if (!equipmentSlot.CardViewHandler)
				{
					list2.Add(equipmentSlot);
				}
				else
				{
					list.Add(equipmentSlot);
				}
			}
			EquipmentSlots.Clear();
			for (int i = 0; i < list.Count; i++)
			{
				EquipmentSlotView value = list[i];
				EquipmentSlots.AddLast(value);
			}
			for (int j = 0; j < list2.Count; j++)
			{
				EquipmentSlotView value2 = list2[j];
				EquipmentSlots.AddLast(value2);
			}
			int num = 0;
			for (LinkedListNode<EquipmentSlotView> linkedListNode = EquipmentSlots.Last; linkedListNode != null; linkedListNode = linkedListNode.Previous)
			{
				linkedListNode.Value.ValidateSlotOccupancy();
				num++;
			}
			num = 0;
			for (LinkedListNode<EquipmentSlotView> linkedListNode = EquipmentSlots.Last; linkedListNode != null; linkedListNode = linkedListNode.Previous)
			{
				EquipmentSlotView value3 = linkedListNode.Value;
				value3.ReOrderCardView(num);
				value3.ReorderSlot(num);
				num++;
			}
			if (IsFolded)
			{
				for (LinkedListNode<EquipmentSlotView> linkedListNode2 = _equipmentSlots.Last; linkedListNode2 != null; linkedListNode2 = linkedListNode2.Previous)
				{
					linkedListNode2.Value.transform.SetSiblingIndex(linkedListNode2.Value.Index);
				}
			}
			if (IsUnFolded)
			{
				for (LinkedListNode<EquipmentSlotView> linkedListNode3 = _equipmentSlots.First; linkedListNode3 != null; linkedListNode3 = linkedListNode3.Next)
				{
					linkedListNode3.Value.transform.SetSiblingIndex(_slotViewsContainer.GetSiblingIndex() + base.transform.childCount - linkedListNode3.Value.Index);
				}
			}
		}

		public async UniTask<bool> Fold(bool instant = false, bool generalFold = false)
		{
			TransitionToFold();
			for (LinkedListNode<EquipmentSlotView> linkedListNode = EquipmentSlots.Last; linkedListNode != null; linkedListNode = linkedListNode.Previous)
			{
				linkedListNode.Value.transform.SetParent(equipmentsVerticalLayout.transform);
				linkedListNode.Value.transform.SetSiblingIndex(linkedListNode.Value.Index);
				SlotViewsContainerLayoutElement.ignoreLayout = true;
				if (instant)
				{
					linkedListNode.Value.CardViewHandler?.CardView?.SnapTransformToTarget();
				}
				linkedListNode.Value.ApplyFoldSlotMask(instant);
			}
			RectTransform slotViewsContainer = SlotViewsContainer;
			slotViewsContainer.anchorMin = new Vector2(0f, 0f);
			slotViewsContainer.anchorMax = new Vector2(1f, 1f);
			slotViewsContainer.pivot = new Vector2(0.5f, 0.5f);
			slotViewsContainer.anchoredPosition = Vector2.zero;
			Vector2 sizeDelta = new Vector2(0f, 0f);
			slotViewsContainer.sizeDelta = sizeDelta;
			raycastTarget.raycastPadding = Vector4.zero;
			if (instant)
			{
				_handView.ExecuteLayoutRebuild();
			}
			if (generalFold)
			{
				if (instant)
				{
					base.transform.localScale = Vector3.one;
					LayoutGroup.padding.bottom = scaleDownBottomPadding;
					LayoutGroup.padding.left = scaleDownLeftPadding;
					LayoutGroup.padding.right = scaleDownRightPadding;
					_handView.ScheduleLayoutRebuild();
					return true;
				}
				await ScaleHandSlotSize(normalScale, scaleNormalDuration);
			}
			return true;
		}

		public async UniTask<bool> UnFold(bool instant = false)
		{
			TransitionToUnFold();
			for (LinkedListNode<EquipmentSlotView> linkedListNode = _equipmentSlots.Last; linkedListNode != null; linkedListNode = linkedListNode.Previous)
			{
				linkedListNode.Value.transform.SetParent(base.transform.parent);
				linkedListNode.Value.transform.SetSiblingIndex(base.transform.GetSiblingIndex() + 1);
				linkedListNode.Value.transform.SetParent(parentTransform.transform);
				linkedListNode.Value.transform.SetSiblingIndex(_slotViewsContainer.GetSiblingIndex() + 1);
				if (instant)
				{
					linkedListNode.Value.CardViewHandler?.CardView?.SnapTransformToTarget();
				}
				linkedListNode.Value.RemoveFoldSlotMask(instant);
			}
			SlotViewsContainerLayoutElement.ignoreLayout = false;
			RectTransform slotViewsContainer = SlotViewsContainer;
			slotViewsContainer.anchorMin = new Vector2(0f, 1f);
			slotViewsContainer.anchorMax = new Vector2(0f, 1f);
			slotViewsContainer.pivot = new Vector2(0f, 1f);
			slotViewsContainer.anchoredPosition = Vector2.zero;
			Vector2 sizeDelta = new Vector2(200f, 0f);
			slotViewsContainer.sizeDelta = sizeDelta;
			raycastTarget.raycastPadding = new Vector4(0f, 0f, raycastTargetExpansionSize, 0f);
			if (instant)
			{
				_handView.ExecuteLayoutRebuild();
			}
			HandView.Slots.Where((PlayerHandSlotView handSlotView) => handSlotView != this).ForEach(delegate(PlayerHandSlotView handSlotView)
			{
				handSlotView.LayoutGroup.padding.bottom = scaleDownBottomPadding;
				handSlotView.LayoutGroup.padding.left = scaleDownLeftPadding;
				handSlotView.LayoutGroup.padding.right = scaleDownRightPadding;
				handSlotView.ScaleHandSlotSize(scaleDownSize, scaleDownDuration).Forget();
			});
			LayoutGroup.padding.bottom = scaleUpBottomPadding;
			LayoutGroup.padding.left = scaleUpLeftPadding;
			LayoutGroup.padding.right = scaleUpRightPadding;
			await ScaleHandSlotSize(scaleUpSize, scaleUpDuration);
			return true;
		}

		private async UniTask ScaleHandSlotSize(float scale, float duration)
		{
			_scaleTween?.Kill();
			try
			{
				_scaleTween = base.transform.DOScale(scale, duration).SetEase(foldScaleEase.GetEaseFunction()).SetUpdate(UpdateType.Late, isIndependentUpdate: true)
					.OnUpdate(delegate
					{
						_handView.ScheduleLayoutRebuild();
					});
				await _scaleTween.ToUniTask(TweenCancelBehaviour.KillAndCancelAwait, base.destroyCancellationToken);
			}
			finally
			{
				_scaleTween = null;
				_handView.ExecuteLayoutRebuild();
			}
		}

		private async UniTask MergeStartAnimation(UICardViewHandler originalCard, UICardViewHandler toDropEquipment, CardSlotView slotView, bool firstMerge = true)
		{
			_ = 5;
			try
			{
				float spiralDuration = _animationSettings.MergeStartSpiralTime;
				float positioningTime = _animationSettings.MergeStartPositioningTime;
				EaseFunction positioningEase = _animationSettings.MergeStartPositioningEase.GetEaseFunction();
				EaseFunction rotationEase = _animationSettings.EquipRotationEase.GetEaseFunction();
				float spiralRadius = UIResolutionHelpers.GetResAdjustedScreenSpaceOffset(_animationSettings.MergeStartSpiralRadius);
				float spiralLaps = _animationSettings.MergeStartSpiralLaps;
				float glowTime = _animationSettings.MergeStartGlowTime;
				originalCard.CardView.DisableTilt();
				toDropEquipment.CardView.DisableTilt();
				originalCard.CardView.KillMagnetLock();
				toDropEquipment.CardView.KillMagnetLock();
				DisableMergeMagnetLockGlow();
				originalCard.CardView.EnableSelectionOuterGlow(state: false);
				originalCard.CardView.HideCompatVFX();
				toDropEquipment.CardView.EnableSelectionOuterGlow(state: false);
				toDropEquipment.CardView.HideCompatVFX();
				_handView.TryHideMergeBubble();
				originalCard.LockMotion(state: true);
				toDropEquipment.LockMotion(state: true);
				await UniTask.NextFrame(base.destroyCancellationToken);
				originalCard.SetParentToOnDragContainer();
				toDropEquipment.SetParentToOnDragContainer();
				UICardPickMenuView.Instance?.SelectedCardViewContainer.TempAddCardView(originalCard.CardView);
				UICardPickMenuView.Instance?.SelectedCardViewContainer.TempAddCardView(toDropEquipment.CardView);
				await UniTask.NextFrame(base.destroyCancellationToken);
				Vector3 defaultScale = Vector3.one;
				await UniTask.NextFrame(base.destroyCancellationToken);
				RuntimeManager.PlayOneShot(mergeSound);
				Vector3 mergePivotPosition = new Vector3((float)Screen.width / 2f, (float)Screen.height / 2f, 0f);
				Vector3 endValue = mergePivotPosition + new Vector3(Mathf.Cos(0f), Mathf.Sin(0f)) * spiralRadius;
				Vector3 endValue2 = mergePivotPosition + new Vector3(Mathf.Cos(MathF.PI), Mathf.Sin(MathF.PI)) * spiralRadius;
				_mergeAnimationTweenSequence?.Kill();
				_mergeAnimationTweenSequence = DOTween.Sequence(this);
				_mergeAnimationTweenSequence.Append(originalCard.CardView.transform.DOMove(endValue, positioningTime).SetEase(positioningEase));
				_mergeAnimationTweenSequence.Join(toDropEquipment.CardView.transform.DOMove(endValue2, positioningTime).SetEase(positioningEase));
				_mergeAnimationTweenSequence.Join(originalCard.CardView.transform.DOScale(defaultScale, positioningTime).SetEase(positioningEase));
				_mergeAnimationTweenSequence.Join(toDropEquipment.CardView.transform.DOScale(defaultScale, positioningTime).SetEase(positioningEase));
				_mergeAnimationTweenSequence.Join(originalCard.CardView.transform.DOLocalRotate(Vector3.zero, positioningTime));
				_mergeAnimationTweenSequence.Join(toDropEquipment.CardView.transform.DOLocalRotate(Vector3.zero, positioningTime));
				if (firstMerge)
				{
					_mergeAnimationTweenSequence.Join(originalCard.CardView.Card3DProxy.Card.RotateOnPlaceEffect(positioningTime, 360f).SetEase(rotationEase));
					_mergeAnimationTweenSequence.Join(toDropEquipment.CardView.Card3DProxy.Card.RotateOnPlaceEffect(positioningTime, 360f).SetEase(rotationEase));
				}
				_mergeAnimationTweenSequence.SetUpdate(UpdateType.Late, isIndependentUpdate: true);
				originalCard.CardView.Card3DProxy.Card.Transform.rotation = Quaternion.identity;
				toDropEquipment.CardView.Card3DProxy.Card.Transform.rotation = Quaternion.identity;
				await _mergeAnimationTweenSequence.ToUniTask(TweenCancelBehaviour.KillAndCancelAwait, base.destroyCancellationToken);
				_mergeAnimationTweenSequence.Kill();
				_mergeAnimationTweenSequence = DOTween.Sequence(this);
				_mergeAnimationTweenSequence.Append(originalCard.CardView.MergeGlowIn(glowTime));
				_mergeAnimationTweenSequence.Join(toDropEquipment.CardView.MergeGlowIn(glowTime));
				_mergeAnimationTweenSequence.SetUpdate(UpdateType.Late, isIndependentUpdate: true);
				_handView.PlayMergeGodRaysVFX(mergePivotPosition);
				float elapsedTime = 0f;
				float maxAngle = 360f * spiralLaps;
				maxAngle *= MathF.PI / 180f;
				while (elapsedTime < spiralDuration)
				{
					float t = elapsedTime / spiralDuration;
					float t2 = _animationSettings.MergeStartSpiralEase.EasePercentage(t);
					float num = Mathf.Lerp(0f, maxAngle, t2);
					float num2 = Mathf.Lerp(spiralRadius, 0f, t2);
					Vector3 vector = new Vector3(Mathf.Cos(num), Mathf.Sin(num), 0f) * num2;
					Vector3 vector2 = new Vector3(Mathf.Cos(num + MathF.PI), Mathf.Sin(num + MathF.PI), 0f) * num2;
					originalCard.CardView.transform.position = mergePivotPosition + vector;
					toDropEquipment.CardView.transform.position = mergePivotPosition + vector2;
					elapsedTime += Time.unscaledDeltaTime;
					await UniTask.NextFrame(base.destroyCancellationToken);
				}
				originalCard.CardView.transform.position = mergePivotPosition;
				toDropEquipment.CardView.transform.position = mergePivotPosition;
				await _mergeAnimationTweenSequence.ToUniTask(TweenCancelBehaviour.KillAndCancelAwait, base.destroyCancellationToken);
			}
			catch (OperationCanceledException)
			{
			}
		}

		private async UniTask MergeRevealAnimation(UICardViewHandler cardViewHandler)
		{
			float mergeEndGlowTime = _animationSettings.MergeEndGlowTime;
			_handView.StopMergeGodRaysVFX();
			_mergeAnimationTweenSequence?.Kill();
			_mergeAnimationTweenSequence = DOTween.Sequence(this);
			cardViewHandler.CardView.PlayMergeParticleSystem();
			_mergeAnimationTweenSequence.Append(cardViewHandler.CardView.MergeGlowOut(mergeEndGlowTime));
			_mergeAnimationTweenSequence.SetUpdate(UpdateType.Late, isIndependentUpdate: true);
			try
			{
				await _mergeAnimationTweenSequence.ToUniTask(TweenCancelBehaviour.KillAndCancelAwait);
			}
			finally
			{
				_mergeAnimationTweenSequence = null;
			}
		}

		private async UniTask MergeEndAnimation(UICardViewHandler cardViewHandler, Vector2 targetPosition)
		{
			EaseFunction easeFunction = _animationSettings.MergeEndPositioningEase.GetEaseFunction();
			float mergeEndPositioningTime = _animationSettings.MergeEndPositioningTime;
			float mergeEndPositioningDelayTime = _animationSettings.MergeEndPositioningDelayTime;
			_mergeAnimationTweenSequence?.Kill();
			_mergeAnimationTweenSequence = DOTween.Sequence(this);
			_mergeAnimationTweenSequence.SetDelay(mergeEndPositioningDelayTime);
			_mergeAnimationTweenSequence.Append(cardViewHandler.CardView.transform.DOMove(targetPosition, mergeEndPositioningTime).SetEase(easeFunction));
			_mergeAnimationTweenSequence.Join(cardViewHandler.CardView.Card3DProxy.Card.Tilt(Vector3.up, 20f, mergeEndPositioningTime));
			_mergeAnimationTweenSequence.SetUpdate(UpdateType.Late, isIndependentUpdate: true);
			try
			{
				await _mergeAnimationTweenSequence.ToUniTask(TweenCancelBehaviour.KillAndCancelAwait, base.destroyCancellationToken);
				cardViewHandler.CardView.EnableStaticRender(state: true);
			}
			finally
			{
				_mergeAnimationTweenSequence = null;
			}
		}

		public void RunCompatibilityCheck(UIEquipmentCardViewHandler equipmentViewHandler)
		{
			if (IsEquipmentCompatible(equipmentViewHandler))
			{
				weaponSlotView.CardViewHandler?.ShowCompatVFX();
			}
			else
			{
				weaponSlotView.CardViewHandler?.ShowUnCompatVFX();
			}
			RunEquipmentCompatibilityCheck(equipmentViewHandler);
		}

		private bool IsEquipmentCompatible(UIEquipmentCardViewHandler equipmentViewHandler)
		{
			return HandSlot.IsEquipmentCompatible(equipmentViewHandler.RuntimeEquipmentData);
		}

		private void RunEquipmentCompatibilityCheck(UIEquipmentCardViewHandler equipmentViewHandler)
		{
			for (int i = 0; i < _equipmentSlots.Count; i++)
			{
				EquipmentSlotView equipmentSlotView = _equipmentSlots.ElementAt(i);
				if (!equipmentSlotView.IsEmpty)
				{
					RuntimeEquipmentData runtimeEquipmentData = equipmentSlotView.CardViewHandler.RuntimeEquipmentData;
					if (!(equipmentSlotView.CardViewHandler == equipmentViewHandler) && RuntimeEquipmentData.CanMerge(runtimeEquipmentData, equipmentViewHandler.RuntimeEquipmentData))
					{
						equipmentSlotView.CardViewHandler.ShowMergeCompatVFX();
					}
				}
			}
		}

		public void HideCompatibilityVFX()
		{
			if (HandSlot.RuntimeWeaponData != null)
			{
				this.HideAllCompatVFX?.Invoke();
			}
		}

		public void ShowSelectionVFX(bool state, bool enableArrow = false)
		{
			if (state)
			{
				bgGlowEffect.Show();
				if (enableArrow)
				{
					selectionIndicator.gameObject.SetActive(value: true);
				}
				else
				{
					selectionIndicator.gameObject.SetActive(value: false);
				}
			}
			else
			{
				bgGlowEffect.Hide();
				selectionIndicator.gameObject.SetActive(value: false);
			}
		}

		public void StartSelectionArrowMovement()
		{
			_slotSelectionArrowTween?.Kill();
			_slotSelectionArrowTween = DOTween.Sequence(this);
			Vector3 vector = Vector3.up * UIResolutionHelpers.GetResAdjustedScreenSpaceOffset(0f);
			Vector3 endValue = Vector3.up * UIResolutionHelpers.GetResAdjustedScreenSpaceOffset(-5f);
			selectionIndicator.rectTransform.localPosition = vector;
			_slotSelectionArrowTween.Append(DOTween.To(() => selectionIndicator.rectTransform.localPosition, delegate(Vector3 newPosition)
			{
				selectionIndicator.rectTransform.localPosition = newPosition;
			}, endValue, 2f).SetEase(Ease.InOutQuad));
			_slotSelectionArrowTween.Append(DOTween.To(() => selectionIndicator.rectTransform.localPosition, delegate(Vector3 newPosition)
			{
				selectionIndicator.rectTransform.localPosition = newPosition;
			}, vector, 2f).SetEase(Ease.InOutQuad));
			_slotSelectionArrowTween.SetLoops(-1, LoopType.Restart);
			_slotSelectionArrowTween.SetUpdate(UpdateType.Late, isIndependentUpdate: true);
		}

		public void StopSelectionArrowMovement()
		{
			_slotSelectionArrowTween?.Kill();
			selectionIndicator.rectTransform.localPosition = Vector3.zero;
		}

		public void StartSelectionMovement()
		{
			_slotSelectionMoveTween?.Kill();
			_slotSelectionMoveTween = DOTween.Sequence(this);
			Vector3 vector = Vector3.up * UIResolutionHelpers.GetResAdjustedScreenSpaceOffset(20f);
			Vector3 endValue = Vector3.up * UIResolutionHelpers.GetResAdjustedScreenSpaceOffset(30f);
			SlotViewsContainer.localPosition = vector;
			_slotSelectionMoveTween.Append(DOTween.To(() => SlotViewsContainer.localPosition, delegate(Vector3 newPosition)
			{
				SlotViewsContainer.localPosition = newPosition;
			}, endValue, 2f).SetEase(Ease.InOutQuad));
			_slotSelectionMoveTween.Append(DOTween.To(() => SlotViewsContainer.localPosition, delegate(Vector3 newPosition)
			{
				SlotViewsContainer.localPosition = newPosition;
			}, vector, 2f).SetEase(Ease.InOutQuad));
			_slotSelectionMoveTween.SetLoops(-1, LoopType.Restart);
			_slotSelectionMoveTween.SetUpdate(UpdateType.Late, isIndependentUpdate: true);
		}

		public void StopSelectionMovement()
		{
			_slotSelectionMoveTween?.Kill();
			SlotViewsContainer.localPosition = Vector3.zero;
		}

		public void DisableMergeMagnetLockGlow()
		{
			for (LinkedListNode<EquipmentSlotView> linkedListNode = _equipmentSlots.Last; linkedListNode != null; linkedListNode = linkedListNode.Previous)
			{
				if (linkedListNode.Value.CardViewHandler != null)
				{
					linkedListNode.Value.CardViewHandler.CardView.ShowMergeMagnetLockVFX(state: false);
				}
			}
		}

		private void StopMagnetContainerAnimations()
		{
			weaponSlotView.StopMagnetContainerAnimation();
			for (LinkedListNode<EquipmentSlotView> linkedListNode = _equipmentSlots.Last; linkedListNode != null; linkedListNode = linkedListNode.Previous)
			{
				if (linkedListNode.Value.CardViewHandler != null)
				{
					linkedListNode.Value.StopMagnetContainerAnimation();
				}
			}
		}

		public void SetEquipmentSlotsInteractable(bool interactable)
		{
			foreach (EquipmentSlotView equipmentSlot in _equipmentSlots)
			{
				equipmentSlot.SetInteractable(interactable);
			}
		}
	}
}
