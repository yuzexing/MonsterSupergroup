using UnityEngine;

namespace AstralShift.QTI.Interactors
{
	public interface IInteractor
	{
		Transform Transform => GetTransform();

		Transform GetTransform();

		Vector3 GetFacingDirection()
		{
			return Transform.forward;
		}

		Vector2 GetFacingDirection2D()
		{
			Vector3 normalized = Vector3.ProjectOnPlane(Transform.forward, Vector3.up).normalized;
			return new Vector2(normalized.x, normalized.z);
		}

		Vector3 GetPosition()
		{
			return Transform.position;
		}

		Vector2 GetPosition2D()
		{
			return new Vector2(Transform.position.x, Transform.position.z);
		}
	}
}
