using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Interactions
{
	[AddComponentMenu("QTI/Interactions/SetPositionInteraction")]
	public class SetPositionInteraction : Interaction
	{
		public enum Mode
		{
			transform = 0,
			position = 1
		}

		public Mode mode;

		public Transform targetObject;

		public Transform newPositionTransform;

		public Vector3 newPosition;

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			if (targetObject != null)
			{
				targetObject.TryGetComponent<CharacterController>(out var component);
				switch (mode)
				{
				case Mode.transform:
					if (newPositionTransform != null)
					{
						if (component != null)
						{
							component.enabled = false;
							targetObject.position = newPositionTransform.position;
							component.enabled = true;
						}
						else
						{
							targetObject.position = newPositionTransform.position;
						}
					}
					else
					{
						Debug.LogError("SetPositionInteraction: newPositionTransform is null!");
					}
					break;
				case Mode.position:
					if (component != null)
					{
						component.enabled = false;
						targetObject.position = newPosition;
						component.enabled = true;
					}
					else
					{
						targetObject.position = newPosition;
					}
					break;
				}
			}
			else
			{
				Debug.LogError("SetPositionInteraction: targetObject is null!");
			}
			OnEnd();
		}
	}
}
