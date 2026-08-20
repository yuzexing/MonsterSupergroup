using UnityEngine;

namespace AstralShift.QTI.Triggers.Physics
{
	[AddComponentMenu("QTI/Triggers/Physics/StepOffTrigger")]
	public class StepOffTrigger : PhysicsTrigger
	{
		private void OnTriggerExit(Collider otherCollider)
		{
			if (FilterInteractor(otherCollider.gameObject, out var interactor))
			{
				base.Interact(interactor);
			}
		}
	}
}
