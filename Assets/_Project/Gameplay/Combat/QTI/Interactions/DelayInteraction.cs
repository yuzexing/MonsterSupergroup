using System.Collections;
using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Interactions
{
	[AddComponentMenu("QTI/Interactions/DelayInteraction")]
	public class DelayInteraction : Interaction
	{
		[Tooltip("Wait time to execute the onEnd interactions (seconds)")]
		[SerializeField]
		protected float waitTime = 1f;

		[Tooltip("Defines if wait time is <Time.timescale> dependent \n \n(Default: false)")]
		[SerializeField]
		protected bool unscaledTime;

		[SerializeField]
		private bool triggerWhenActive;

		public float WaitTime
		{
			get
			{
				return waitTime;
			}
			set
			{
				waitTime = value;
			}
		}

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			if (triggerWhenActive || base.isActiveAndEnabled)
			{
				if (unscaledTime)
				{
					StartCoroutine(UnscaledWait(WaitTime));
				}
				else
				{
					StartCoroutine(Wait(WaitTime));
				}
			}
		}

		public IEnumerator UnscaledWait(float time)
		{
			yield return new WaitForSecondsRealtime(time);
			OnEnd();
		}

		public IEnumerator Wait(float time)
		{
			yield return new WaitForSeconds(time);
			OnEnd();
		}
	}
}
