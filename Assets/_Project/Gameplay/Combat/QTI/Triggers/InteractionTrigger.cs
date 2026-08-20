using AstralShift.QTI.Interactions;
using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Triggers
{
	public abstract class InteractionTrigger : MonoBehaviour
	{
		public delegate void TriggerActivation();

		public Interaction interaction;

		private TriggerActivation _activateTrigger;

		protected int CurrentActivationCount;

		protected int MaxActivationCount;

		protected virtual void Awake()
		{
		}

		public virtual Vector2 GetPosition2D()
		{
			return new Vector2(base.transform.position.x, base.transform.position.z);
		}

		public virtual Vector2 GetFacingDirection2D()
		{
			Vector3 vector = Vector3.ProjectOnPlane(base.transform.forward, Vector3.up);
			return new Vector2(vector.x, vector.z);
		}

		public virtual Vector3 GetFacingDirection()
		{
			return Vector3.ProjectOnPlane(base.transform.forward, Vector3.up);
		}

		public virtual void Interact(IInteractor interactor)
		{
			if (!(interaction == null) && interaction.isActiveAndEnabled)
			{
				MaxActivationCount = GetLeafCount(interaction);
				_activateTrigger = ActivateTrigger;
				base.enabled = false;
				interaction.Interact(interactor, _activateTrigger);
			}
		}

		protected int GetLeafCount(Interaction interaction)
		{
			if (interaction is ConditionInteraction conditionInteraction)
			{
				int num = 0;
				if (conditionInteraction.onTrueInteractions == null || conditionInteraction.onTrueInteractions.Count == 1)
				{
					num = 1;
				}
				else
				{
					foreach (Interaction onTrueInteraction in conditionInteraction.onTrueInteractions)
					{
						num += GetLeafCount(onTrueInteraction);
					}
				}
				if (conditionInteraction.onFalseInteractions == null || conditionInteraction.onFalseInteractions.Count == 1)
				{
					num = 1;
				}
				else
				{
					foreach (Interaction onFalseInteraction in conditionInteraction.onFalseInteractions)
					{
						num += GetLeafCount(onFalseInteraction);
					}
				}
				return num;
			}
			if (interaction.onEndInteractions == null)
			{
				return 1;
			}
			if (interaction.onEndInteractions.Count == 0)
			{
				return 1;
			}
			int num2 = 0;
			foreach (Interaction onEndInteraction in interaction.onEndInteractions)
			{
				num2 += GetLeafCount(onEndInteraction);
			}
			return num2;
		}

		public void ActivateTrigger()
		{
			CurrentActivationCount++;
			if (CurrentActivationCount == MaxActivationCount)
			{
				CurrentActivationCount = 0;
				base.enabled = true;
			}
		}
	}
}
