using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Interactions
{
	public class SetRotationInteraction : Interaction
	{
		public enum Mode
		{
			Euler = 0,
			AngleAxis = 1
		}

		public enum Axis
		{
			Up = 0,
			Right = 1,
			Front = 2
		}

		public Transform targetObject;

		public Mode mode;

		public Axis axis;

		public float angle;

		public Vector3 newRotation;

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			if (targetObject != null)
			{
				switch (mode)
				{
				case Mode.Euler:
					targetObject.localRotation = Quaternion.Euler(newRotation);
					break;
				case Mode.AngleAxis:
					switch (axis)
					{
					case Axis.Up:
						targetObject.localRotation = Quaternion.AngleAxis(targetObject.localRotation.eulerAngles.y + angle, Vector3.up);
						break;
					case Axis.Right:
						targetObject.localRotation = Quaternion.AngleAxis(targetObject.localRotation.eulerAngles.x + angle, Vector3.right);
						break;
					case Axis.Front:
						targetObject.rotation = Quaternion.AngleAxis(targetObject.localRotation.eulerAngles.z + angle, Vector3.forward);
						break;
					}
					break;
				}
			}
			else
			{
				Debug.LogError("SetRotationInteraction: targetObject is null!");
			}
			OnEnd();
		}
	}
}
