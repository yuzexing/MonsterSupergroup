using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Interactions.Demos.TPDemo
{
	public class DamageInteraction : Interaction, IInteractor
	{
		[SerializeField]
		private int damage;

		public Transform GetTransform()
		{
			return base.transform;
		}

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			Damage(interactor);
			OnEnd();
		}

		private void Damage(IInteractor interactor)
		{
			if (interactor != null)
			{
				Debug.Log("DAMAGED " + interactor.ToString());
				if (interactor is IDamageable)
				{
					(interactor as IDamageable).TakeDamage(damage);
				}
			}
		}
	}
}
