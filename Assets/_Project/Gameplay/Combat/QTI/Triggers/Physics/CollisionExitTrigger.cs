using UnityEngine;

namespace AstralShift.QTI.Triggers.Physics
{
	[AddComponentMenu("QTI/Triggers/Physics/CollisionExitTrigger")]
	public class CollisionExitTrigger : PhysicsTrigger
	{
		protected override void Awake()
		{
			base.Awake();
			RefreshCollider();
		}

		private void OnCollisionExit(Collision otherCollision)
		{
			if (FilterInteractor(otherCollision.gameObject, out var interactor))
			{
				base.Interact(interactor);
			}
		}

		public override void RefreshCollider()
		{
			base.RefreshCollider();
			_collider.isTrigger = false;
		}
	}
}
