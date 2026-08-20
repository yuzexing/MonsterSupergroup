using AstralShift.QTI.Helpers;
using UnityEngine;

namespace AstralShift.QTI.Triggers.Physics
{
	[AddComponentMenu("QTI/Triggers/Physics/StayOnTrigger")]
	public class StayOnTrigger : PhysicsTrigger
	{
		[SerializeField]
		private bool hasCooldown;

		[SerializeField]
		private float cooldownTimer = 1f;

		private float _elapsedTime = float.PositiveInfinity;

		private bool _canInteract = true;

		private void OnTriggerStay(Collider otherCollider)
		{
			if (_elapsedTime < cooldownTimer)
			{
				_elapsedTime += Time.deltaTime;
				_canInteract = _elapsedTime >= cooldownTimer;
			}
			if ((!hasCooldown || _canInteract) && FilterInteractor(otherCollider.gameObject, out var interactor))
			{
				base.Interact(interactor);
				_canInteract = false;
				_elapsedTime = 0f;
			}
		}

		private void OnTriggerExit(Collider other)
		{
			if (PhysicsHelper.ContainsLayer(other.gameObject.layer, layerMask))
			{
				_canInteract = true;
				_elapsedTime = float.PositiveInfinity;
			}
		}
	}
}
