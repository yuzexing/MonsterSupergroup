using UnityEngine;

namespace AstralShift.QTI.Triggers.Physics2D
{
	[AddComponentMenu("QTI/Triggers/Physics2D/StepOn2DTrigger")]
	public class StepOn2DTrigger : Physics2DTrigger
	{
		private void OnTriggerEnter2D(Collider2D otherCollider)
		{
			if (FilterInteractor(otherCollider.gameObject, out var interactor))
			{
				base.Interact(interactor);
			}
		}
	}
}
