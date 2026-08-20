using System;
using System.Collections.Generic;
using System.Linq;
using AstralShift.FSM;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.UI.Cards;
using Coffee.UIExtensions;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI.Menus.Hand
{
	public class PlayerHandView : UIFocusListener
	{
		private LinkedList<PlayerHandSlotView> _slots;

		[SerializeField]
		protected HorizontalLayoutGroup layoutGroup;

		[SerializeField]
		protected float foldedSpacing;

		[SerializeField]
		protected float unFoldedSpacing;

		[Header("VFX")]
		[SerializeField]
		protected UIParticle mergeGodRaysParticleSystem;

		private UIParticle _mergeGodRaysInstance;

		[SerializeField]
		protected CanvasGroup mergeBubble;

		private CanvasGroup _mergeBubbleInstance;

		private Tween _mergeBubbleShowTween;

		private StateMachine _stateMachine;

		private State _unFolded;

		private State _folded;

		private PlayerHandSlotView _currentUnFoldedSlot;

		private RectTransform _rectTransform;

		private bool _isAnySlotAnimating;

		private List<UniTask> _foldTasks;

		private List<UniTask> _unfoldTasks;

		public LinkedList<PlayerHandSlotView> Slots => _slots;

		public RectTransform RectTransform
		{
			get
			{
				if (!_rectTransform)
				{
					_rectTransform = GetComponent<RectTransform>();
				}
				return _rectTransform;
			}
		}

		public bool HasWeapons => PlayerHand.Instance.WeaponCount > 0;

		public bool HasEquipments => Slots.Any((PlayerHandSlotView slot) => slot.HandSlot.HasEquipments());

		public event Action OnFold;

		public event Action OnBeforeUnFold;

		public event Action OnUnFold;

		public async UniTask Init()
		{
			_slots = new LinkedList<PlayerHandSlotView>();
			PlayerHandSlotView[] componentsInChildren = GetComponentsInChildren<PlayerHandSlotView>(includeInactive: true);
			for (int i = 0; i < componentsInChildren.Length; i++)
			{
				PlayerHandSlotView playerHandSlotView = componentsInChildren[i];
				PlayerHandSlot handSlotFromIndex = PlayerHand.Instance.GetHandSlotFromIndex(i);
				playerHandSlotView.Init(this, handSlotFromIndex);
				Slots.AddLast(playerHandSlotView);
			}
			InitializeStateMachine();
			foreach (PlayerHandSlotView slot in Slots)
			{
				await slot.InstantiatePreEquippedCards();
			}
		}

		private void LateUpdate()
		{
			if (_isAnySlotAnimating)
			{
				LayoutRebuilder.MarkLayoutForRebuild(RectTransform);
			}
		}

		private void OnDestroy()
		{
			if (_mergeGodRaysInstance != null)
			{
				UnityEngine.Object.Destroy(_mergeGodRaysInstance.gameObject);
			}
		}

		private void InitializeStateMachine()
		{
			_stateMachine = new StateMachine("Player Hand View");
			_folded = new State("Folded");
			_unFolded = new State("UnFolded");
			State folded = _folded;
			folded.onEnter = (Action)Delegate.Combine(folded.onEnter, (Action)delegate
			{
				this.OnFold?.Invoke();
			});
			State unFolded = _unFolded;
			unFolded.onEnter = (Action)Delegate.Combine(unFolded.onEnter, (Action)delegate
			{
				this.OnUnFold?.Invoke();
			});
			InitializeStateMachineTransitions();
		}

		private void InitializeStateMachineTransitions()
		{
			_stateMachine.AddTransition(_folded, _unFolded);
			_stateMachine.AddTransition(_unFolded, _folded);
			_stateMachine.SetInitialStateNoCallbacks(_unFolded);
		}

		public PlayerHandSlotView GetHandSlotFromIndex(int index)
		{
			return Slots.ElementAt(index);
		}

		public LinkedListNode<PlayerHandSlotView> GetHandSlotNode(PlayerHandSlotView view)
		{
			return Slots.Find(view);
		}

		public LinkedListNode<PlayerHandSlotView> GetHandSlotNodeFromIndex(int index)
		{
			return Slots.Find(Slots.ElementAt(index));
		}

		public PlayerHandSlotView GetPreviousHandSlotView(PlayerHandSlotView view)
		{
			return GetHandSlotNode(view).Previous?.Value;
		}

		public PlayerHandSlotView GetNextHandSlotView(PlayerHandSlotView view)
		{
			return GetHandSlotNode(view).Next?.Value;
		}

		public PlayerHandSlotView GetFirstHandSlotWithEquipments()
		{
			foreach (PlayerHandSlotView slot in Slots)
			{
				if (slot.HandSlot.HasWeapon() && slot.HandSlot.HasEquipments())
				{
					return slot;
				}
			}
			return null;
		}

		public PlayerHandSlotView GetFirstHandSlotWithWeapons()
		{
			foreach (PlayerHandSlotView slot in Slots)
			{
				if (slot.HandSlot.HasWeapon())
				{
					return slot;
				}
			}
			return null;
		}

		public bool CanMagnetLockToAnySlot(UICardViewHandler cardView)
		{
			foreach (PlayerHandSlotView slot in Slots)
			{
				if (slot.CanMagnetLock(cardView))
				{
					return true;
				}
			}
			return false;
		}

		public bool CanMagnetLockToAnySlot(UICardViewHandler cardView, out PlayerHandSlotView slotView)
		{
			foreach (PlayerHandSlotView slot in Slots)
			{
				if (slot.CanMagnetLock(cardView))
				{
					slotView = slot;
					return true;
				}
			}
			slotView = null;
			return false;
		}

		public async UniTask<PlayerHandSlotView> TryMagnetLockFirstAvailableSlot(UICardViewHandler cardView)
		{
			PlayerHandSlotView result = null;
			foreach (PlayerHandSlotView slot in Slots)
			{
				if (await slot.TryMagnetLock(cardView))
				{
					result = slot;
					break;
				}
			}
			return result;
		}

		public async UniTask<PlayerHandSlotView> TryMagnetLockPreviousAvailableSlot(PlayerHandSlotView startSlot, UICardViewHandler cardView, bool ignoreAssignedSlot = true)
		{
			if (startSlot == null)
			{
				return null;
			}
			LinkedListNode<PlayerHandSlotView> linkedListNode = GetHandSlotNode(startSlot);
			if (linkedListNode.Previous == null)
			{
				return null;
			}
			while (linkedListNode.Previous != null)
			{
				linkedListNode = linkedListNode.Previous;
				PlayerHandSlotView currentSlot = linkedListNode.Value;
				if (currentSlot.CanMagnetLock(cardView, ignoreAssignedSlot))
				{
					startSlot.TryStopMagnetLock(cardView);
					await currentSlot.TryMagnetLock(cardView, ignoreAssignedSlot);
					return currentSlot;
				}
			}
			return null;
		}

		public async UniTask<PlayerHandSlotView> TryMagnetLockNextAvailableSlot(PlayerHandSlotView startSlot, UICardViewHandler cardView, bool ignoreAssignedSlot = true)
		{
			if (startSlot == null)
			{
				return null;
			}
			LinkedListNode<PlayerHandSlotView> linkedListNode = GetHandSlotNode(startSlot);
			if (linkedListNode.Next == null)
			{
				return null;
			}
			while (linkedListNode.Next != null)
			{
				linkedListNode = linkedListNode.Next;
				PlayerHandSlotView currentSlot = linkedListNode.Value;
				if (currentSlot.CanMagnetLock(cardView, ignoreAssignedSlot))
				{
					startSlot.TryStopMagnetLock(cardView);
					await currentSlot.TryMagnetLock(cardView, ignoreAssignedSlot);
					return currentSlot;
				}
			}
			return null;
		}

		public void AbortAllMagnetLocks(UICardViewHandler cardView)
		{
			foreach (PlayerHandSlotView slot in Slots)
			{
				slot.TryStopMagnetLock(cardView);
			}
		}

		public async UniTask FoldHand(bool instant = false)
		{
			if (_folded == _stateMachine.GetState())
			{
				return;
			}
			_currentUnFoldedSlot = null;
			_stateMachine.MakeTransition(_folded);
			layoutGroup.spacing = foldedSpacing;
			_isAnySlotAnimating = !instant;
			try
			{
				await UniTask.WhenAll(Slots.Select((PlayerHandSlotView slot) => slot.Fold(instant, generalFold: true)));
			}
			finally
			{
				_isAnySlotAnimating = false;
				ExecuteLayoutRebuild();
			}
		}

		public async UniTask UnfoldHand(PlayerHandSlotView handSlotView, bool instant = false)
		{
			if (_unFolded == _stateMachine.GetState() && _currentUnFoldedSlot == handSlotView)
			{
				return;
			}
			_currentUnFoldedSlot = handSlotView;
			_stateMachine.MakeTransition(_unFolded);
			layoutGroup.spacing = unFoldedSpacing;
			layoutGroup.padding.bottom = 10;
			_isAnySlotAnimating = !instant;
			try
			{
				this.OnBeforeUnFold?.Invoke();
				await UniTask.WhenAll(Slots.Select((PlayerHandSlotView slot) => (!(slot == handSlotView)) ? slot.Fold(instant) : slot.UnFold(instant)));
			}
			finally
			{
				ExecuteLayoutRebuild();
			}
		}

		public void ExecuteLayoutRebuild()
		{
			_isAnySlotAnimating = false;
			LayoutRebuilder.ForceRebuildLayoutImmediate(RectTransform);
		}

		public void ScheduleLayoutRebuild()
		{
			_isAnySlotAnimating = true;
		}

		public void SwapSlotAfter(int index)
		{
			LinkedListNode<PlayerHandSlotView> handSlotNodeFromIndex = GetHandSlotNodeFromIndex(index);
			LinkedListNode<PlayerHandSlotView> next = handSlotNodeFromIndex.Next;
			if (next != null)
			{
				Slots.Remove(handSlotNodeFromIndex);
				Slots.AddAfter(next, new LinkedListNode<PlayerHandSlotView>(handSlotNodeFromIndex.Value));
				handSlotNodeFromIndex.Value.transform.SetSiblingIndex(index + 1);
				PlayerHand.Instance.MoveAfter(index);
				ConstructHandNavigation();
			}
		}

		public void SwapSlotBefore(int index)
		{
			LinkedListNode<PlayerHandSlotView> handSlotNodeFromIndex = GetHandSlotNodeFromIndex(index);
			LinkedListNode<PlayerHandSlotView> previous = handSlotNodeFromIndex.Previous;
			if (previous != null)
			{
				Slots.Remove(handSlotNodeFromIndex);
				Slots.AddBefore(previous, new LinkedListNode<PlayerHandSlotView>(handSlotNodeFromIndex.Value));
				handSlotNodeFromIndex.Value.transform.SetSiblingIndex(index - 1);
				PlayerHand.Instance.MoveBefore(index);
				ConstructHandNavigation();
			}
		}

		public void SetEquipmentSlotsInteractable(bool interactable)
		{
			foreach (PlayerHandSlotView slot in Slots)
			{
				slot.SetEquipmentSlotsInteractable(interactable);
			}
		}

		public void RunCompatibilityCheck(UIEquipmentCardViewHandler equipmentViewHandler)
		{
			foreach (PlayerHandSlotView slot in Slots)
			{
				if (slot.HandSlot.HasWeapon())
				{
					slot.RunCompatibilityCheck(equipmentViewHandler);
				}
			}
		}

		public void ConstructHandNavigation()
		{
			foreach (PlayerHandSlotView slot in Slots)
			{
				slot.ConstructEquipmentsNavigation();
			}
			ConstructHandSlotsNavigation();
			UIEquipmentCardViewHandler uIEquipmentCardViewHandler = null;
			foreach (PlayerHandSlotView slot2 in Slots)
			{
				UIEquipmentCardViewHandler firstFoundEquipmentView = slot2.GetFirstFoundEquipmentView();
				if (firstFoundEquipmentView != null && uIEquipmentCardViewHandler != null)
				{
					uIEquipmentCardViewHandler.InputHandler.SetRightNavigation(firstFoundEquipmentView.InputHandler);
					firstFoundEquipmentView.InputHandler.SetLeftNavigation(uIEquipmentCardViewHandler.InputHandler);
				}
				UIEquipmentCardViewHandler lastFoundEquipmentView = slot2.GetLastFoundEquipmentView();
				if (lastFoundEquipmentView != null)
				{
					uIEquipmentCardViewHandler = lastFoundEquipmentView;
				}
			}
		}

		public void ConstructHandSlotsNavigation()
		{
			ClearHandSlotsNavigation();
			PlayerHandSlotView playerHandSlotView = null;
			for (int i = 1; i < Slots.Count; i++)
			{
				PlayerHandSlotView handSlotFromIndex = GetHandSlotFromIndex(i - 1);
				if (handSlotFromIndex.HandSlot.HasWeapon())
				{
					playerHandSlotView = handSlotFromIndex;
				}
				PlayerHandSlotView handSlotFromIndex2 = GetHandSlotFromIndex(i);
				if (handSlotFromIndex2.HandSlot.HasWeapon() && playerHandSlotView != null)
				{
					playerHandSlotView.GamepadHandler.SetRightNavigation(handSlotFromIndex2.GamepadHandler);
					handSlotFromIndex2.GamepadHandler.SetLeftNavigation(playerHandSlotView.GamepadHandler);
					playerHandSlotView = handSlotFromIndex2;
				}
			}
		}

		public void ClearHandSlotsNavigation()
		{
			foreach (PlayerHandSlotView slot in Slots)
			{
				slot.GamepadHandler.ClearNavigation();
			}
		}

		public void HideSortDeckSelectionVFX()
		{
			foreach (PlayerHandSlotView slot in Slots)
			{
				slot.HideSortDeckSelectionVFX();
			}
		}

		public void ResetSwapModePositions()
		{
			foreach (PlayerHandSlotView slot in Slots)
			{
				slot.GamepadHandler.ResetPosition();
			}
		}

		public override void OnFocusEnter()
		{
		}

		public override void OnFocusExit()
		{
			FoldHand();
		}

		public void HideCompatibilityVFX()
		{
			foreach (PlayerHandSlotView slot in Slots)
			{
				slot.HideCompatibilityVFX();
			}
		}

		public UIParticle PlayMergeGodRaysVFX(Vector3 position)
		{
			if (_mergeGodRaysInstance == null)
			{
				_mergeGodRaysInstance = UnityEngine.Object.Instantiate(mergeGodRaysParticleSystem, position, Quaternion.identity, UICardPickMenuView.Instance.FrontVisualsContainer.Transform);
				_mergeGodRaysInstance.transform.SetSiblingIndex(0);
			}
			else
			{
				_mergeGodRaysInstance.transform.position = position;
			}
			_mergeGodRaysInstance.Clear();
			_mergeGodRaysInstance.Play();
			_mergeGodRaysInstance.StartEmission();
			return _mergeGodRaysInstance;
		}

		public CanvasGroup ShowMergeBubble(Transform parent)
		{
			if (_mergeBubbleInstance == null)
			{
				_mergeBubbleInstance = UnityEngine.Object.Instantiate(mergeBubble, parent, worldPositionStays: false);
				_mergeBubbleInstance.transform.localPosition = Vector3.zero;
				_mergeBubbleInstance.transform.localScale = Vector3.one;
				_mergeBubbleInstance.alpha = 0f;
				_mergeBubbleShowTween?.Kill();
				_mergeBubbleShowTween = _mergeBubbleInstance.DOFade(1f, 0.15f).SetUpdate(UpdateType.Late, isIndependentUpdate: true);
			}
			else
			{
				_mergeBubbleInstance.transform.SetParent(parent, worldPositionStays: true);
				_mergeBubbleInstance.transform.localPosition = Vector3.zero;
				_mergeBubbleInstance.transform.localScale = Vector3.one;
				_mergeBubbleShowTween?.Kill();
				_mergeBubbleShowTween = _mergeBubbleInstance.DOFade(1f, 0.15f).SetUpdate(UpdateType.Late, isIndependentUpdate: true);
			}
			return _mergeBubbleInstance;
		}

		public void TryHideMergeBubble()
		{
			if (!(_mergeBubbleInstance == null))
			{
				_mergeBubbleInstance.transform.SetParent(UICardPickMenuView.Instance.FrontVisualsContainer.Transform, worldPositionStays: true);
				_mergeBubbleInstance.transform.localScale = Vector3.one;
				_mergeBubbleShowTween?.Kill();
				_mergeBubbleShowTween = _mergeBubbleInstance.DOFade(0f, 0.1f).SetUpdate(UpdateType.Late, isIndependentUpdate: true).SetLink(_mergeBubbleInstance.gameObject);
			}
		}

		public void StopMergeGodRaysVFX()
		{
			if (_mergeGodRaysInstance != null)
			{
				_mergeGodRaysInstance.StopEmission();
			}
		}
	}
}
