using UnityEngine;

namespace AstralShift.QTI.Triggers.Physics
{
	[AddComponentMenu("QTI/Triggers/Physics/StepOnTrigger")]
	public class StepOnTrigger : PhysicsTrigger
	{
		private void OnTriggerEnter(Collider otherCollider)
		{
			if (FilterInteractor(otherCollider.gameObject, out var interactor))
			{
				base.Interact(interactor);
			}
		}
	}
}
