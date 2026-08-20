using AstralShift.QTI.Interactors;

namespace AstralShift.QTI.Interactions.Demos.TPDemo
{
	public class CollectInteraction : Interaction
	{
		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			if (interactor is PlayerControllerDemo && (interactor as PlayerControllerDemo).TryGetComponent<Collector>(out var component))
			{
				component.Collect();
			}
			OnEnd();
		}
	}
}
