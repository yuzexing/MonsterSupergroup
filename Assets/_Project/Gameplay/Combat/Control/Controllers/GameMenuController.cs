using System;
using Animancer;
using AstralShift.FSM;
using AstralShift.HellMaiden;
using AstralShift.HellMaiden.UI;
using AstralShift.UI;
using Rewired;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;

namespace AstralShift.Control.Controllers
{
	public abstract class GameMenuController : UIController
	{
		[SerializeField]
		protected Canvas canvas;

		[SerializeField]
		protected AnimancerComponent menuAnimator;

		[SerializeField]
		protected ClipTransition openAnimation;

		[SerializeField]
		protected ClipTransition closeAnimation;

		[SerializeField]
		protected StateMachine stateMachine;

		protected State Disabled;

		protected State Opening;

		protected State Active;

		protected State Closing;

		protected State Quitting;

		[Header("Events")]
		[SerializeField]
		protected UnityEngine.Events.UnityEvent onOpen;

		[SerializeField]
		protected UnityEngine.Events.UnityEvent onClose;

		protected UISelectable firstSelectable;

		protected UISelectable currentSelectable;

		protected bool IsActive
		{
			get
			{
				if (stateMachine != null)
				{
					return stateMachine.GetState() == Active;
				}
				return false;
			}
		}

		public event Action OnCloseOnce;

		public event Action OnOpenOnce;

		public override void Init()
		{
			stateMachine = new StateMachine("GameMenuController: " + base.name);
			Disabled = new State("Disabled");
			Opening = new State("Opening");
			Active = new State("Active");
			Closing = new State("Closing");
			Quitting = new State("Quitting");
			stateMachine.AddTransition(Disabled, Opening);
			stateMachine.AddTransition(Opening, Active);
			stateMachine.AddTransition(Active, Closing);
			stateMachine.AddTransition(Closing, Disabled);
			stateMachine.AddTransition(Quitting, Disabled);
			stateMachine.AddAnyTransition(Active);
			stateMachine.AddAnyTransition(Quitting);
			InitDefaultStateBehaviour();
			InitStateBehaviour();
			stateMachine.SetInitialStateNoCallbacks(Disabled);
			EnableCanvas(state: false);
		}

		protected void InitDefaultStateBehaviour()
		{
			State disabled = Disabled;
			disabled.onEnter = (Action)Delegate.Combine(disabled.onEnter, (Action)delegate
			{
				EnableCanvas(state: false);
			});
			State opening = Opening;
			opening.onEnter = (Action)Delegate.Combine(opening.onEnter, (Action)delegate
			{
				EnableCanvas(state: true);
				onOpen.Invoke();
				ref Action onEnd = ref openAnimation.Events.OnEnd;
				onEnd = (Action)Delegate.Combine(onEnd, new Action(OnOpeningFinished));
			});
			State closing = Closing;
			closing.onEnter = (Action)Delegate.Combine(closing.onEnter, (Action)delegate
			{
				onClose.Invoke();
				ref Action onEnd = ref closeAnimation.Events.OnEnd;
				onEnd = (Action)Delegate.Combine(onEnd, new Action(OnClosingFinished));
			});
			State quitting = Quitting;
			quitting.onEnter = (Action)Delegate.Combine(quitting.onEnter, (Action)delegate
			{
				GameDirector.Instance.QuittingMenu = true;
			});
		}

		protected abstract void InitStateBehaviour();

		public virtual void Open()
		{
			OpenAnimation();
		}

		protected virtual void OpenAnimation()
		{
			stateMachine.MakeTransition(Opening);
			menuAnimator.Layers[0].Play(openAnimation, openAnimation.FadeDuration);
		}

		public virtual void Close()
		{
			CloseAnimation();
		}

		protected virtual void CloseAnimation()
		{
			stateMachine.MakeTransition(Closing);
			menuAnimator.Layers[0].Play(closeAnimation, closeAnimation.FadeDuration);
		}

		protected virtual void OnOpeningFinished()
		{
			ref Action onEnd = ref openAnimation.Events.OnEnd;
			onEnd = (Action)Delegate.Remove(onEnd, new Action(OnOpeningFinished));
			this.OnOpenOnce?.Invoke();
			this.OnOpenOnce = null;
			stateMachine.MakeTransition(Active);
		}

		protected virtual void OnClosingFinished()
		{
			ref Action onEnd = ref closeAnimation.Events.OnEnd;
			onEnd = (Action)Delegate.Remove(onEnd, new Action(OnClosingFinished));
			this.OnCloseOnce?.Invoke();
			this.OnCloseOnce = null;
			stateMachine.MakeTransition(Disabled);
		}

		protected virtual void OnControllerTypeChanged()
		{
			if (currentSelectable != null)
			{
				if (ControllerLifetime.ActiveControllerType != ControllerType.Mouse)
				{
					EventSystem.current.SetSelectedGameObject(currentSelectable.gameObject);
				}
				else
				{
					EventSystem.current.SetSelectedGameObject(null);
				}
			}
			else if (firstSelectable != null)
			{
				if (ControllerLifetime.ActiveControllerType != ControllerType.Mouse)
				{
					EventSystem.current.SetSelectedGameObject(firstSelectable.gameObject);
				}
				else
				{
					EventSystem.current.SetSelectedGameObject(null);
				}
			}
			else
			{
				EventSystem.current.SetSelectedGameObject(null);
			}
		}

		public override void Activate()
		{
			if (GameDirector.Instance.QuittingMenu)
			{
				stateMachine.MakeTransition(Quitting);
			}
			ControllerLifetime.EnableMouseDeadzone = true;
			base.Activate();
			ControllerLifetime.OnControllerChanged += OnControllerTypeChanged;
			ControllerLifetime.OnControllerChanged += PointerManager.Instance.SetPointerForMenuNavigation;
		}

		public override void Deactivate()
		{
			ControllerLifetime.EnableMouseDeadzone = false;
			base.Deactivate();
			ControllerLifetime.OnControllerChanged -= OnControllerTypeChanged;
			ControllerLifetime.OnControllerChanged -= PointerManager.Instance.SetPointerForMenuNavigation;
		}

		protected virtual void EnableCanvas(bool state)
		{
			if ((bool)canvas)
			{
				canvas.enabled = state;
			}
		}

		protected virtual void EnableGameObject(bool state)
		{
			base.gameObject.SetActive(state);
		}
	}
}
