using AstralShift.QTI.Interactions;
using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.HellMaiden.Interactions
{
	public class SuctionInteraction : Interaction
	{
		[SerializeField]
		private float magnitude = 1.5f;

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			Vector2 vector = (base.gameObject.transform.GetChild(0).position - GameDirector.Instance.Player.transform.position).normalized;
			GameDirector.Instance.Player.DebuffForce = vector * magnitude;
			OnEnd();
		}
	}
}
