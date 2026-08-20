using AstralShift.QTI.Helpers;
using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Triggers.Physics
{
	[AddComponentMenu("QTI/Triggers/Physics/CollisionStayTrigger")]
	public class CollisionStayTrigger : PhysicsTrigger
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

		private void OnCollisionStay(Collision otherCollision)
		{
			if (_elapsedTime < cooldownTimer)
			{
				_elapsedTime += Time.deltaTime;
				_canInteract = _elapsedTime >= cooldownTimer;
			}
			if ((!hasCooldown || _canInteract) && base.enabled && PhysicsHelper.ContainsLayer(otherCollision.gameObject.layer, layerMask) && otherCollision.gameObject.TryGetComponent<IInteractor>(out var component))
			{
				base.Interact(component);
				_canInteract = false;
				_elapsedTime = 0f;
			}
		}

		private void OnCollisionExit(Collision otherCollision)
		{
			if (FilterInteractor(otherCollision.gameObject, out var interactor))
			{
				base.Interact(interactor);
				_canInteract = true;
				_elapsedTime = float.PositiveInfinity;
			}
		}

		public override void RefreshCollider()
		{
			if (_collider == null)
			{
				_collider = base.gameObject.GetComponent<Collider>();
				if (_collider == null)
				{
					return;
				}
			}
			_collider.isTrigger = false;
		}
	}
}
