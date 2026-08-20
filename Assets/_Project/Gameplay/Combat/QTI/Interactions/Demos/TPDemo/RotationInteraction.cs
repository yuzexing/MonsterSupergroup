using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Interactions.Demos.TPDemo
{
	public class RotationInteraction : Interaction
	{
		public float turnSpeed = 1f;

		public Rigidbody rb;

		public PlayerControllerDemo player;

		private IInteractor playerInteractor;

		public bool invertDirection;

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			playerInteractor = player;
			Vector3 normalized = (new Vector3(playerInteractor.GetPosition2D().x, rb.transform.position.y, playerInteractor.GetPosition2D().y) - rb.position).normalized;
			Quaternion b = Quaternion.LookRotation(invertDirection ? (-normalized) : normalized);
			rb.MoveRotation(Quaternion.Slerp(rb.rotation, b, turnSpeed * Time.fixedDeltaTime));
			OnEnd();
		}
	}
}
