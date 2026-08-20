using UnityEngine;

namespace AstralShift.QTI.Triggers.Physics2D
{
	[AddComponentMenu("QTI/Triggers/Physics2D/StepOff2DTrigger")]
	public class StepOff2DTrigger : Physics2DTrigger
	{
		private void OnTriggerExit2D(Collider2D otherCollider)
		{
			if (FilterInteractor(otherCollider.gameObject, out var interactor))
			{
				base.Interact(interactor);
			}
		}
	}
}
