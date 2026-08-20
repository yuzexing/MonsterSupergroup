using System;
using AstralShift.FSM;
using FMODUnity;
using UnityEngine;

namespace AstralShift.HellMaiden.UI
{
	public abstract class UIBaseFocusElement : MonoBehaviour
	{
		[SerializeField]
		protected CanvasGroup canvasGroup;

		[SerializeField]
		protected CustomAnimationCurve moveEase;

		[SerializeField]
		protected CustomAnimationCurve scaleEase;

		[SerializeField]
		protected float duration = 0.5f;

		protected StateMachine _stateMachine;

		protected State Focused;

		protected State Unfocused;

		[SerializeField]
		private EventReference focusSound;

		public event Action OnFocusGained;

		public event Action OnFocusLost;

		private void Awake()
		{
			_stateMachine = new StateMachine(base.gameObject.name);
			Focused = new State("Focused");
			Unfocused = new State("Unfocused");
			_stateMachine.AddTransition(Focused, Unfocused);
			_stateMachine.AddTransition(Unfocused, Focused);
			State focused = Focused;
			focused.onEnter = (Action)Delegate.Combine(focused.onEnter, new Action(OnFocusEnter));
			State unfocused = Unfocused;
			unfocused.onEnter = (Action)Delegate.Combine(unfocused.onEnter, new Action(OnUnFocusEnter));
			_stateMachine.SetInitialStateNoCallbacks(Unfocused);
		}

		public abstract void OnFocusEnter();

		public abstract void OnUnFocusEnter();

		public virtual void Focus()
		{
			_stateMachine.MakeTransition(Focused);
			if (_stateMachine.GetState() == Focused)
			{
				this.OnFocusGained?.Invoke();
				RuntimeManager.PlayOneShot(focusSound);
			}
		}

		public virtual void UnFocus()
		{
			_stateMachine.MakeTransition(Unfocused);
			if (_stateMachine.GetState() == Unfocused)
			{
				this.OnFocusLost?.Invoke();
			}
		}

		public void Enable()
		{
			if (!(canvasGroup == null))
			{
				canvasGroup.interactable = true;
			}
		}

		public void Disable()
		{
			if (!(canvasGroup == null))
			{
				canvasGroup.interactable = false;
			}
		}
	}
}
