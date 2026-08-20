using System.Collections.Generic;
using AstralShift.QTI.Interactors;
using AstralShift.QTI.Triggers;
using UnityEngine;

namespace AstralShift.QTI.Interactions
{
	public abstract class Interaction : MonoBehaviour
	{
		public List<Interaction> onEndInteractions = new List<Interaction>();

		protected IInteractor _interactor;

		protected InteractionTrigger.TriggerActivation _triggerActivation;

		public virtual void Interact(IInteractor interactor, InteractionTrigger.TriggerActivation triggerActivation)
		{
			_triggerActivation = triggerActivation;
			Interact(interactor);
		}

		public virtual void Interact(IInteractor interactor)
		{
			_interactor = interactor;
		}

		public void Interact()
		{
			Interact(null);
		}

		public void OnEnd()
		{
			if (onEndInteractions.Count > 0)
			{
				foreach (Interaction onEndInteraction in onEndInteractions)
				{
					if (!onEndInteraction || !onEndInteraction.gameObject)
					{
						Debug.LogError("Interaction is Null: chain broken! Remove all Empty elements from the OnEndInteractions of every Interaction in the chain!");
						_triggerActivation?.Invoke();
						break;
					}
					onEndInteraction.Interact(_interactor, _triggerActivation);
				}
				return;
			}
			_triggerActivation?.Invoke();
		}

		public virtual bool CanInteract()
		{
			return true;
		}
	}
}
