using UnityEngine;

namespace AstralShift.QTI.Triggers.Physics2D
{
	[AddComponentMenu("QTI/Triggers/Physics2D/CollisionExit2DTrigger")]
	public class CollisionExit2DTrigger : Physics2DTrigger
	{
		protected override void Awake()
		{
			base.Awake();
			RefreshCollider();
		}

		private void OnCollisionExit2D(Collision2D otherCollision)
		{
			if (FilterInteractor(otherCollision.collider.gameObject, out var interactor))
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
