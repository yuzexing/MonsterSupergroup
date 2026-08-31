using AstralShift.QTI.Helpers;
using UnityEngine;

namespace AstralShift.QTI.Triggers.Physics2D
{
	[AddComponentMenu("QTI/Triggers/Physics2D/StayOn2DTrigger")]
	public class StayOn2DTrigger : Physics2DTrigger
	{
		[SerializeField]
		private bool hasCooldown;

		[SerializeField]
		private float cooldownTimer = 1f;

		private float _elapsedTime = float.PositiveInfinity;

		private bool _canInteract = true;

		
		private void OnTriggerEnter2D(Collider2D other)
		{
			Debug.Log($"ENTER: {name} <- {other.name}", this);
		}

		private void OnTriggerStay2D(Collider2D otherCollider)
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

		private void OnTriggerExit2D(Collider2D other)
		{
			if (PhysicsHelper.ContainsLayer(other.gameObject.layer, layerMask))
			{
				_canInteract = true;
				_elapsedTime = float.PositiveInfinity;
			}
		}
	}
}
