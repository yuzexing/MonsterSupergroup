using AstralShift.QTI.Triggers.Physics;

namespace AstralShift.QTI.Interactors
{
	public interface IInputInteractor : IInteractor
	{
		InputTrigger GetInteraction();

		bool TryInteract();
	}
}
