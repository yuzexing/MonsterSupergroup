using UnityEngine;

namespace AstralShift.QTI.Interactions.Demos.FPPuzzleDemo
{
	public interface IGravityField
	{
		Vector3 GetMovementDelta()
		{
			return Vector3.zero;
		}
	}
}
