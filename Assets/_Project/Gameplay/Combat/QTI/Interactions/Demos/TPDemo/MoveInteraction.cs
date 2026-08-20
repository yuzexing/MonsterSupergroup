using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Interactions.Demos.TPDemo
{
	public class MoveInteraction : Interaction
	{
		public float speed = 1f;

		public Rigidbody rb;

		public PlayerControllerDemo player;

		private IInteractor playerInteractor;

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			playerInteractor = player;
			Vector3 position = Vector3.MoveTowards(target: new Vector3(playerInteractor.GetPosition2D().x, rb.transform.position.y, playerInteractor.GetPosition2D().y), current: rb.position, maxDistanceDelta: speed * Time.fixedDeltaTime);
			rb.MovePosition(position);
			OnEnd();
		}
	}
}
