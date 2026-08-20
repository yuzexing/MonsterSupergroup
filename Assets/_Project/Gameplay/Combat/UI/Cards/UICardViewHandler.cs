using System;
using AstralShift.Control;
using AstralShift.FSM;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.UI.Menus;
using AstralShift.HellMaiden.UI.Menus.Hand;
using Cysharp.Threading.Tasks;
using FMODUnity;
using I2.Loc;
using Rewired;
using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI.Cards
{
	public abstract class UICardViewHandler : MonoBehaviour, ICardVisual
	{
		[Header("References")]
		[SerializeField]
		private Canvas canvas;

		[SerializeField]
		protected UICardView cardView;

		[SerializeField]
		protected CanvasGroup canvasGroup;

		[SerializeField]
		protected GraphicRaycaster raycaster;

		protected int _siblingIndex;

		protected Transform _transform;

		private RectTransform _rectTransform;

		private int _defaultOrderInLayer;

		protected Transform _onDragContainer;

		protected Transform _onIdleContainer;

		protected StateMachine _stateMachine;

		protected State _idleState;

		protected State _draggingState;

		protected State _droppedState;

		protected State _disabledState;

		protected bool _hasBeenDropped;

		protected RuntimeCardData _runtimeCardData;

		[SerializeField]
		private UICardViewMouseHandler _mouseHandler;

		[SerializeField]
		private UICardViewGamepadHandler _gamepadHandler;

		[SerializeField]
		private UICardViewInputHandler _inputHandler;

		[Header("Sounds")]
		[SerializeField]
		protected EventReference onSelectSound;

		public Canvas Canvas => canvas;

		public UICardView CardView => cardView;

		public CardSlotView CardSlot { get; protected set; }

		public int SiblingIndex => _siblingIndex;

		public Transform Transform
		{
			get
			{
				if (_transform == null)
				{
					_transform = base.transform;
				}
				return _transform;
			}
		}

		public RectTransform RectTransform
		{
			get
			{
				if (!_rectTransform)
				{
					TryGetComponent<RectTransform>(out _rectTransform);
				}
				return _rectTransform;
			}
		}

		public Vector3 GlobalScale => Transform.lossyScale / ((Canvas != null) ? Canvas.scaleFactor : 1f);

		public bool IsIdle
		{
			get
			{
				if (_stateMachine != null)
				{
					return _stateMachine.GetState() == _idleState;
				}
				return false;
			}
		}

		public bool IsDragging
		{
			get
			{
				if (_stateMachine != null)
				{
					return _stateMachine.GetState() == _draggingState;
				}
				return false;
			}
		}

		public bool IsDropped
		{
			get
			{
				if (_stateMachine != null)
				{
					return _stateMachine.GetState() == _droppedState;
				}
				return false;
			}
		}

		public bool HasBeenDropped => _hasBeenDropped;

		public RuntimeCardData RuntimeCardData => _runtimeCardData;

		public UICardViewMouseHandler MouseHandler
		{
			get
			{
				return _mouseHandler;
			}
			set
			{
				_mouseHandler = value;
			}
		}

		public UICardViewGamepadHandler GamepadHandler
		{
			get
			{
				return _gamepadHandler;
			}
			set
			{
				_gamepadHandler = value;
			}
		}

		public UICardViewInputHandler InputHandler
		{
			get
			{
				return _inputHandler;
			}
			set
			{
				_inputHandler = value;
			}
		}

		public event Action OnEnterIdleCallback;

		public event Action OnExitIdleCallback;

		public event Action OnEnterDraggingCallback;

		public event Action OnExitDraggingCallback;

		public event Action OnEnterDroppedCallback;

		public event Action OnExitDroppedCallback;

		public virtual void Initialize(RuntimeCardData runtimeCardData)
		{
			_runtimeCardData = runtimeCardData;
			_onIdleContainer = Transform.parent;
			CardView.Init(this);
			ControllerLifetime.OnBeforeControllerChanged += SwitchInputHandler;
			LocalizationManager.OnLocalizeEvent += LocalizeText;
			InitializeStateMachine();
		}

		private async void LocalizeText()
		{
			try
			{
				await CardVisualsFactory.RefreshUICardText(this).AttachExternalCancellation(base.destroyCancellationToken);
				if ((bool)CardView)
				{
					CardView.EnqueueRender();
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception arg)
			{
				Debug.LogWarning($"Localization failed for {base.name}: {arg}");
			}
		}

		public virtual void ReInitialize(RuntimeCardData runtimeCardData)
		{
			_runtimeCardData = runtimeCardData;
			InitializeStateMachine();
		}

		private void SwitchInputHandler(ControllerType controllerType)
		{
			if (!InputHandler && ((bool)MouseHandler || (bool)GamepadHandler))
			{
				if (controllerType == ControllerType.Mouse)
				{
					GamepadHandler.enabled = false;
					MouseHandler.enabled = true;
				}
				else
				{
					MouseHandler.enabled = false;
					GamepadHandler.enabled = true;
				}
			}
		}

		private void OnDestroy()
		{
			this.OnEnterIdleCallback = null;
			this.OnExitIdleCallback = null;
			this.OnEnterDraggingCallback = null;
			this.OnExitDraggingCallback = null;
			this.OnEnterDroppedCallback = null;
			this.OnExitDroppedCallback = null;
			ControllerLifetime.OnBeforeControllerChanged -= SwitchInputHandler;
			LocalizationManager.OnLocalizeEvent -= LocalizeText;
			CardView.Dispose();
		}

		public void Show()
		{
			CardView.Show();
		}

		public void Hide()
		{
			CardView.Hide();
		}

		public void LockMotion(bool state)
		{
			if (state)
			{
				CardView.LockAllMotion();
			}
			else
			{
				CardView.UnlockAllMotion();
			}
		}

		public void AllowInteraction(bool value)
		{
			canvasGroup.interactable = value;
			canvasGroup.blocksRaycasts = value;
			raycaster.enabled = value;
		}

		public virtual void SetSiblingIndex(int index)
		{
			_siblingIndex = index;
			Transform.SetSiblingIndex(_siblingIndex);
			CardView.SetSiblingIndex(_siblingIndex);
			CardView.RefreshIdleAnimationOffset();
		}

		public virtual void SetOnDragContainer(Transform container)
		{
			_onDragContainer = container;
		}

		public void SetParentToOnDragContainer()
		{
			Transform.SetParent(_onDragContainer, worldPositionStays: true);
			Transform.localScale = Vector3.one;
		}

		public void SetParentContainer(Transform transform)
		{
			Transform.SetParent(transform, worldPositionStays: true);
			Transform.localScale = Vector3.one;
		}

		public virtual void Equip(CardSlotView cardSlot)
		{
			DropInSlot(cardSlot);
		}

		public void DropInSlot(CardSlotView cardSlot)
		{
			AssignCardSlot(cardSlot);
			TransitionToDropped();
		}

		public virtual void AssignCardSlot(CardSlotView cardSlot)
		{
			CardSlot = cardSlot;
			CardSlot.AssignCard(this);
		}

		protected virtual void InitializeStateMachine()
		{
			_stateMachine = new StateMachine("UICardViewHandler");
			_idleState = new State("Idle");
			_draggingState = new State("Dragging");
			_droppedState = new State("Dropped");
			_disabledState = new State("Disabled");
			InitializeStateCallbacks();
			InitializeStateTransitions();
		}

		protected virtual void InitializeStateCallbacks()
		{
			_idleState.onEnter = OnEnterIdle;
			_idleState.onExit = OnExitIdle;
			_draggingState.onEnter = OnEnterDragging;
			_draggingState.onExit = OnExitDragging;
			_droppedState.onEnter = OnEnterDropped;
			_droppedState.onExit = OnExitDropped;
		}

		protected virtual void InitializeStateTransitions()
		{
			_stateMachine.AddTransition(_idleState, _draggingState);
			_stateMachine.AddTransition(_draggingState, _idleState);
			_stateMachine.AddTransition(_draggingState, _droppedState);
			_stateMachine.AddTransition(_droppedState, _draggingState);
			_stateMachine.AddAnyTransition(_disabledState);
			_stateMachine.SetInitialStateNoCallbacks(_idleState);
		}

		protected virtual void OnEnterIdle()
		{
			Transform.SetParent(_onIdleContainer);
			Transform.SetSiblingIndex(CardView.SiblingIndex);
			Transform.localPosition = Vector3.zero;
			Transform.localScale = Vector3.one;
			UICardPickMenuView.Instance?.SetOfferingsLayoutGroupEnable(state: true);
			CardView.EnableIdleAnimation(state: true);
			AllowInteraction(value: true);
			EnableRarityVFX(state: true);
			this.OnEnterIdleCallback?.Invoke();
		}

		protected virtual void OnExitIdle()
		{
			CardView.EnableIdleAnimation(state: false);
			this.OnExitIdleCallback?.Invoke();
		}

		protected virtual void OnEnterDropped()
		{
			_hasBeenDropped = true;
			if ((bool)CardSlot)
			{
				CardView.KillMagnetLock();
				CardView.DisableRotationFollow();
				CardSlot.ReturnAssignedCard();
			}
			AllowInteraction(value: true);
			this.OnEnterDroppedCallback?.Invoke();
			UICardPickMenuView.Instance?.SetOfferingsLayoutGroupEnable(state: true);
			UICardPickMenuView.Instance?.HandView.SetEquipmentSlotsInteractable(interactable: true);
		}

		protected virtual void OnExitDropped()
		{
			CardView.EnableRotationFollow();
			this.OnExitDroppedCallback?.Invoke();
		}

		protected virtual void OnEnterDragging()
		{
			AllowInteraction(value: false);
			CardView.UnHover();
			CardView.EnableSelectionOuterGlow(state: false);
			CardView.EnableRarityVFX(state: false);
			if (!HasBeenDropped)
			{
				UICardPickMenuView.Instance?.SetOfferingsLayoutGroupEnable(state: false);
				SetParentToOnDragContainer();
			}
			this.OnEnterDraggingCallback?.Invoke();
			UICardPickMenuView.Instance?.SelectedCardViewContainer.TempAddCardView(CardView);
			UICardPickMenuView.Instance?.HandView.SetEquipmentSlotsInteractable(interactable: false);
		}

		protected virtual void OnExitDragging()
		{
			this.OnExitDraggingCallback?.Invoke();
			UICardPickMenuView.Instance?.HandView.SetEquipmentSlotsInteractable(interactable: true);
		}

		private void TransitionToIdle()
		{
			_stateMachine.MakeTransition(_idleState);
		}

		public void TransitionToDragging()
		{
			_stateMachine.MakeTransition(_draggingState);
		}

		public void TransitionToIdleOrDropped()
		{
			if (HasBeenDropped)
			{
				TransitionToDropped();
			}
			else
			{
				TransitionToIdle();
			}
		}

		public void TransitionToDropped()
		{
			_stateMachine.MakeTransition(_droppedState);
		}

		public void TransitionToDisabled()
		{
			_stateMachine.MakeTransition(_disabledState);
		}

		public void SetSelectionVFX(Material material)
		{
			CardView.SetSelectionVFX(material);
		}

		public abstract void HideCompatVFX();

		public abstract void Select();

		public abstract void UnSelect();

		public void EnableRarityVFX(bool state)
		{
			CardView.EnableRarityVFX(state);
		}

		public void SetIllustrationMainLayer(Sprite sprite, Material material = null)
		{
			CardView.Card3DProxy.Card.SetIllustrationMainLayer(sprite, material);
		}

		public UniTask SetIllustrationAdditionalLayer(int index, Sprite sprite, Material material = null)
		{
			return CardView.Card3DProxy.Card.SetIllustrationAdditionalLayer(index, sprite, material);
		}

		public void ClearIllustrationAdditionalLayers()
		{
			CardView.Card3DProxy.Card.ClearIllustrationAdditionalLayers();
		}

		public UniTask SetForegroundLayer(Sprite sprite, Material material = null)
		{
			return CardView.Card3DProxy.Card.SetForegroundLayer(sprite, material);
		}

		public void SetFrameLayer(Sprite bg, Sprite frame, Material bgMaterial = null, Material frameMaterial = null)
		{
			CardView.Card3DProxy.Card.SetFrameLayer(bg, frame, bgMaterial, frameMaterial);
		}

		public void SetTitleText(string text, Color color)
		{
			CardView.Card3DProxy.Card.SetTitleText(text, color);
		}

		public void SetTextBoxBackground(Sprite sprite)
		{
			CardView.Card3DProxy.Card.SetTextBoxBackground(sprite);
		}

		public void SetDescriptionText(string text, Color color)
		{
			CardView.Card3DProxy.Card.SetDescriptionText(text, color);
		}

		public void SetQuoteText(string text, Color color, Color separatorColor)
		{
			CardView.Card3DProxy.Card.SetQuoteText(text, color, separatorColor);
		}

		public virtual void SetLevelIcon(Sprite sprite)
		{
		}

		public virtual void SetEffectIcon(Sprite sprite)
		{
		}
	}
}
