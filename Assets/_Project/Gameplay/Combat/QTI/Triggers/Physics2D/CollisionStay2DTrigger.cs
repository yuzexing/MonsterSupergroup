using AstralShift.QTI.Helpers;
using UnityEngine;

namespace AstralShift.QTI.Triggers.Physics2D
{
	[AddComponentMenu("QTI/Triggers/Physics2D/CollisionStay2DTrigger")]
	public class CollisionStay2DTrigger : Physics2DTrigger
	{
		[SerializeField]
		private bool hasCooldown;

		[SerializeField]
		private float cooldownTimer = 1f;

		private float _elapsedTime = float.PositiveInfinity;

		private bool _canInteract = true;

		protected override void Awake()
		{
			base.Awake();
			RefreshCollider();
		}

		private void OnCollisionStay2D(Collision2D otherCollision)
		{
			if (_elapsedTime < cooldownTimer)
			{
				_elapsedTime += Time.deltaTime;
				_canInteract = _elapsedTime >= cooldownTimer;
			}
			if ((!hasCooldown || _canInteract) && FilterInteractor(otherCollision.collider.gameObject, out var interactor))
			{
				base.Interact(interactor);
				_canInteract = false;
				_elapsedTime = 0f;
			}
		}

		private void OnCollisionExit2D(Collision2D otherCollision)
		{
			if (PhysicsHelper.ContainsLayer(otherCollision.gameObject.layer, layerMask))
			{
				_canInteract = true;
				_elapsedTime = float.PositiveInfinity;
			}
		}

		public override void RefreshCollider()
		{
			if (_collider == null)
			{
				_collider = base.gameObject.GetComponent<Collider2D>();
				if (_collider == null)
				{
					return;
				}
			}
			_collider.isTrigger = false;
		}
	}
}
