using FMODUnity;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace AstralShift.UI
{
	public class UISelectable : Selectable, IPointerEnterHandler, IEventSystemHandler, IPointerExitHandler
	{
		public UnityEvent onSelect;

		public UnityEvent onDeSelect;

		public UnityEvent onPointerEnter;

		public UnityEvent onPointerExit;

		public Animator highlight;

		public bool lockHiglight;

		private readonly int HighlightBool = Animator.StringToHash("Show");

		[SerializeField]
		private EventReference onSelectSound;

		public override void OnSelect(BaseEventData eventData)
		{
			base.OnSelect(eventData);
			if (!onSelectSound.IsNull)
			{
				RuntimeManager.PlayOneShot(onSelectSound);
			}
			onSelect.Invoke();
			Highlight();
		}

		public override void OnDeselect(BaseEventData eventData)
		{
			base.OnDeselect(eventData);
			onDeSelect.Invoke();
			DeHighlight();
		}

		public override void OnPointerEnter(PointerEventData eventData)
		{
			base.OnPointerEnter(eventData);
			if (!onSelectSound.IsNull)
			{
				RuntimeManager.PlayOneShot(onSelectSound);
			}
			onPointerEnter.Invoke();
			Highlight();
		}

		public override void OnPointerExit(PointerEventData eventData)
		{
			base.OnPointerExit(eventData);
			onPointerExit.Invoke();
			DeHighlight();
		}

		public virtual void Highlight()
		{
			if ((bool)highlight && (bool)highlight.runtimeAnimatorController && !lockHiglight)
			{
				highlight.SetBool(HighlightBool, value: true);
			}
		}

		public void DeHighlight()
		{
			if ((bool)highlight && (bool)highlight.runtimeAnimatorController && !lockHiglight)
			{
				highlight.SetBool(HighlightBool, value: false);
			}
		}

		public void LockHighlightState(bool state)
		{
			lockHiglight = state;
		}
	}
}
