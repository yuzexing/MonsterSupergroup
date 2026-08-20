using System.Collections.Generic;
using AstralShift.QTI.Helpers.Attributes;
using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Interactions
{
	[AddComponentMenu("QTI/Interactions/Condition/ConditionInteraction")]
	public class ConditionInteraction : Interaction
	{
		public bool useConditionClass;

		[SerializeField]
		[ConditionalHide("useConditionClass", true)]
		private Condition condition;

		[ConditionalHide("useConditionClass", false)]
		public bool isTrue;

		public List<Interaction> onTrueInteractions = new List<Interaction>();

		public List<Interaction> onFalseInteractions = new List<Interaction>();

		public bool IsTrue
		{
			get
			{
				return isTrue;
			}
			set
			{
				isTrue = value;
			}
		}

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			if (useConditionClass)
			{
				if (condition.Verify(interactor))
				{
					RunInteractionList(onTrueInteractions);
				}
				else
				{
					RunInteractionList(onFalseInteractions);
				}
			}
			else if (isTrue)
			{
				RunInteractionList(onTrueInteractions);
			}
			else
			{
				RunInteractionList(onFalseInteractions);
			}
		}

		private void RunInteractionList(List<Interaction> interactions)
		{
			if (interactions.Count > 0)
			{
				foreach (Interaction interaction in interactions)
				{
					if ((bool)interaction && (bool)interaction.gameObject)
					{
						if (!interaction.gameObject.activeSelf)
						{
							Debug.Log("Object not Active: Interaction will be ignored!");
						}
						else
						{
							interaction.Interact(_interactor, _triggerActivation);
						}
					}
				}
				return;
			}
			_triggerActivation?.Invoke();
		}
	}
}
