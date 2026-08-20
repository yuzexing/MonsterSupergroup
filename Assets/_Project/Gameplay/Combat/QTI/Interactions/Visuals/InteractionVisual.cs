using UnityEngine;

namespace AstralShift.QTI.Interactions.Visuals
{
	public class InteractionVisual : MonoBehaviour
	{
		public string HighlightBool = "Highlight";

		public string DisableBool = "Disable";

		public string InteractTrigger = "Interact";

		public Animator animator;

		public virtual void Awake()
		{
			Idle();
		}

		public virtual void Idle()
		{
			if (!(animator == null))
			{
				animator.SetBool(HighlightBool, value: false);
			}
		}

		public virtual void Highlight()
		{
			if (!(animator == null))
			{
				animator.SetBool(HighlightBool, value: true);
			}
		}

		public virtual void Disable()
		{
			if (!(animator == null))
			{
				animator.SetBool(DisableBool, value: true);
			}
		}

		public virtual void Enable()
		{
			if (!(animator == null))
			{
				animator.SetBool(DisableBool, value: false);
			}
		}

		public virtual void Interact()
		{
			if (!(animator == null))
			{
				animator.SetTrigger(InteractTrigger);
				animator.SetBool(HighlightBool, value: true);
			}
		}
	}
}
