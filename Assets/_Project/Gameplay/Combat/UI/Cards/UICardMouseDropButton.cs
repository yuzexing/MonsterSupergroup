using System;
using System.Collections;
using AstralShift.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI.Cards
{
	[RequireComponent(typeof(Selectable), typeof(CanvasGroup))]
	public class UICardMouseDropButton : UIFadable, IDropHandler, IEventSystemHandler, IPointerEnterHandler, IPointerExitHandler
	{
		[Flags]
		protected enum AnimationTriggerMode
		{
			OnDrop = 0,
			OnStay = 1,
			OnEnter = 2,
			OnExit = 3
		}

		[SerializeField]
		[HideInInspector]
		protected Selectable _selectable;

		[SerializeField]
		protected float hoverDuration = 2f;

		[Header("Normal Animation")]
		[SerializeField]
		protected bool triggerNormalAnimation;

		[SerializeField]
		protected AnimationTriggerMode normalAnimationTriggerMode;

		[Header("Highlighted Animation")]
		[SerializeField]
		protected bool triggerHighlightedAnimation;

		[SerializeField]
		protected AnimationTriggerMode highlightedAnimationTriggerMode;

		[Header("Pressed Animation")]
		[SerializeField]
		protected bool triggerPressedAnimation;

		[SerializeField]
		protected AnimationTriggerMode pressedAnimationTriggerMode;

		[Header("Disabled Animation")]
		[SerializeField]
		protected bool triggerDisabledAnimation;

		[SerializeField]
		protected AnimationTriggerMode disabledAnimationTriggerMode;

		private UICardViewHandler _foundCardViewHandler;

		private Coroutine _pointerStayCoroutine;

		public bool interactable
		{
			get
			{
				if (_selectable == null)
				{
					TryGetComponent<Selectable>(out _selectable);
				}
				return _selectable.interactable;
			}
			set
			{
				if (_selectable == null)
				{
					TryGetComponent<Selectable>(out _selectable);
				}
				_selectable.interactable = value;
			}
		}

		public event Action<UICardViewHandler> OnEnterCallback;

		public event Action<UICardViewHandler> OnStayCallback;

		public event Action<UICardViewHandler> OnExitCallback;

		public event Action<UICardViewHandler> OnDropCallback;

		protected override void Awake()
		{
			base.Awake();
			TryGetComponent<Selectable>(out _selectable);
			TryGetComponent<CanvasGroup>(out _canvasGroup);
			_pointerStayCoroutine = null;
		}

		public void RemoveAllListeners()
		{
			this.OnEnterCallback = null;
			this.OnExitCallback = null;
			this.OnStayCallback = null;
			this.OnDropCallback = null;
		}

		public void SetHoverTime(float time)
		{
			hoverDuration = time;
		}

		public void OnDrop(PointerEventData eventData)
		{
			if (base.isActiveAndEnabled && !(eventData.pointerDrag == null) && eventData.pointerDrag.TryGetComponent<UICardViewHandler>(out _foundCardViewHandler))
			{
				this.OnDropCallback?.Invoke(_foundCardViewHandler);
				if (triggerNormalAnimation && normalAnimationTriggerMode.HasFlag(AnimationTriggerMode.OnDrop))
				{
					_selectable.animator.SetTrigger(_selectable.animationTriggers.normalTrigger);
				}
				if (triggerHighlightedAnimation && highlightedAnimationTriggerMode.HasFlag(AnimationTriggerMode.OnDrop))
				{
					_selectable.animator.SetTrigger(_selectable.animationTriggers.highlightedTrigger);
				}
				if (triggerPressedAnimation && pressedAnimationTriggerMode.HasFlag(AnimationTriggerMode.OnDrop))
				{
					_selectable.animator.SetTrigger(_selectable.animationTriggers.pressedTrigger);
				}
				if (triggerDisabledAnimation && disabledAnimationTriggerMode.HasFlag(AnimationTriggerMode.OnDrop))
				{
					_selectable.animator.SetTrigger(_selectable.animationTriggers.disabledTrigger);
				}
			}
		}

		public void OnPointerEnter(PointerEventData eventData)
		{
			if (base.isActiveAndEnabled && !(eventData.pointerDrag == null) && eventData.pointerDrag.TryGetComponent<UICardViewHandler>(out _foundCardViewHandler))
			{
				this.OnEnterCallback?.Invoke(_foundCardViewHandler);
				if (_pointerStayCoroutine != null)
				{
					StopCoroutine(_pointerStayCoroutine);
				}
				_pointerStayCoroutine = StartCoroutine(PointerStay());
				if (triggerNormalAnimation && normalAnimationTriggerMode.HasFlag(AnimationTriggerMode.OnEnter))
				{
					_selectable.animator.SetTrigger(_selectable.animationTriggers.normalTrigger);
				}
				if (triggerHighlightedAnimation && highlightedAnimationTriggerMode.HasFlag(AnimationTriggerMode.OnEnter))
				{
					_selectable.animator.SetTrigger(_selectable.animationTriggers.highlightedTrigger);
				}
				if (triggerPressedAnimation && pressedAnimationTriggerMode.HasFlag(AnimationTriggerMode.OnEnter))
				{
					_selectable.animator.SetTrigger(_selectable.animationTriggers.pressedTrigger);
				}
				if (triggerDisabledAnimation && disabledAnimationTriggerMode.HasFlag(AnimationTriggerMode.OnEnter))
				{
					_selectable.animator.SetTrigger(_selectable.animationTriggers.disabledTrigger);
				}
			}
		}

		public void OnPointerExit(PointerEventData eventData)
		{
			if (base.isActiveAndEnabled && !(eventData.pointerDrag == null) && eventData.pointerDrag.TryGetComponent<UICardViewHandler>(out _foundCardViewHandler))
			{
				if (_pointerStayCoroutine != null)
				{
					StopCoroutine(_pointerStayCoroutine);
				}
				this.OnExitCallback?.Invoke(_foundCardViewHandler);
				if (triggerNormalAnimation && normalAnimationTriggerMode.HasFlag(AnimationTriggerMode.OnExit))
				{
					_selectable.animator.SetTrigger(_selectable.animationTriggers.normalTrigger);
				}
				if (triggerHighlightedAnimation && highlightedAnimationTriggerMode.HasFlag(AnimationTriggerMode.OnExit))
				{
					_selectable.animator.SetTrigger(_selectable.animationTriggers.highlightedTrigger);
				}
				if (triggerPressedAnimation && pressedAnimationTriggerMode.HasFlag(AnimationTriggerMode.OnExit))
				{
					_selectable.animator.SetTrigger(_selectable.animationTriggers.pressedTrigger);
				}
				if (triggerDisabledAnimation && disabledAnimationTriggerMode.HasFlag(AnimationTriggerMode.OnExit))
				{
					_selectable.animator.SetTrigger(_selectable.animationTriggers.disabledTrigger);
				}
			}
		}

		private IEnumerator PointerStay()
		{
			float timer = 0f;
			while (timer < hoverDuration)
			{
				this.OnStayCallback?.Invoke(_foundCardViewHandler);
				if (triggerNormalAnimation && normalAnimationTriggerMode.HasFlag(AnimationTriggerMode.OnStay))
				{
					_selectable.animator.SetTrigger(_selectable.animationTriggers.normalTrigger);
				}
				if (triggerHighlightedAnimation && highlightedAnimationTriggerMode.HasFlag(AnimationTriggerMode.OnStay))
				{
					_selectable.animator.SetTrigger(_selectable.animationTriggers.highlightedTrigger);
				}
				if (triggerPressedAnimation && pressedAnimationTriggerMode.HasFlag(AnimationTriggerMode.OnStay))
				{
					_selectable.animator.SetTrigger(_selectable.animationTriggers.pressedTrigger);
				}
				if (triggerDisabledAnimation && disabledAnimationTriggerMode.HasFlag(AnimationTriggerMode.OnStay))
				{
					_selectable.animator.SetTrigger(_selectable.animationTriggers.disabledTrigger);
				}
				timer += Time.unscaledDeltaTime;
				yield return null;
			}
			_pointerStayCoroutine = null;
		}
	}
}
