using AstralShift.QTI.Triggers.Physics2D;
using UnityEngine;

namespace AstralShift.QTI.Interactors
{
	public interface IInput2DInteractor : IInteractor
	{
		Input2DTrigger GetInteraction();

		bool TryInteract();

		new Vector3 GetFacingDirection()
		{
			return Transform.up;
		}

		new Vector2 GetFacingDirection2D()
		{
			return Transform.up;
		}

		new Vector3 GetPosition()
		{
			return Transform.position;
		}

		new Vector2 GetPosition2D()
		{
			return Transform.position;
		}
	}
}
