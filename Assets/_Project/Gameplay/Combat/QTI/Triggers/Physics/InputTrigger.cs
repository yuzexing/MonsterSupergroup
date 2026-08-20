using AstralShift.QTI.Helpers;
using AstralShift.QTI.Interactions.Visuals;
using AstralShift.QTI.Interactors;
using AstralShift.QTI.Settings;
using UnityEngine;

namespace AstralShift.QTI.Triggers.Physics
{
	[AddComponentMenu("QTI/Triggers/Physics/InputTrigger")]
	public class InputTrigger : PhysicsTrigger
	{
		public PrioritiesEnumSelector priority;

		public bool isFixedAngle;

		[Range(0f, 360f)]
		public float interactionDirection;

		protected Vector3 _forwardDirection;

		[Range(0f, 360f)]
		public float interactionAngle = 210f;

		protected float _currentFacingAngle;

		protected float _currentRelativeAngle;

		public InteractionVisual interactionVisual;

		public float CurrentFacingAngle => _currentFacingAngle;

		public float CurrentRelativeAngle => _currentRelativeAngle;

		public IInputInteractor ClosestInteractor { get; set; }

		public override void Interact(IInteractor interactor)
		{
			if (interaction == null)
			{
				Debug.LogError("No Interaction assigned to Trigger!!");
			}
			else if (base.enabled && (bool)interaction && interaction.enabled)
			{
				if (CanInteract(interactor.GetFacingDirection2D(), GetPosition2D()))
				{
					base.Interact(interactor);
					interactionVisual?.Interact();
				}
				else
				{
					Debug.Log("Incorrect facing interactionDirection.");
				}
			}
		}

		public virtual bool CanInteract(Vector2 direction, Vector2 position)
		{
			if (!interaction.enabled || !interaction.CanInteract())
			{
				ResetVisuals();
				return false;
			}
			Vector2 directionAtoB = Math.GetDirectionAtoB(position, GetPosition2D());
			bool flag = false;
			if (isFixedAngle)
			{
				_forwardDirection = Quaternion.AngleAxis(interactionDirection, base.transform.up) * GetFacingDirection();
				_currentFacingAngle = Vector2.Angle(new Vector2(_forwardDirection.x, _forwardDirection.z), direction);
				_currentRelativeAngle = Vector2.Angle(new Vector2(_forwardDirection.x, _forwardDirection.z), directionAtoB);
				flag = _currentFacingAngle <= interactionAngle / 2f && _currentRelativeAngle <= interactionAngle / 2f;
			}
			else
			{
				_currentFacingAngle = Vector2.Angle(directionAtoB, direction);
				flag = _currentFacingAngle <= interactionAngle / 2f;
			}
			if (!flag)
			{
				ResetVisuals();
			}
			return flag;
		}

		public void HighlightVisuals()
		{
			if (base.enabled && interaction.enabled)
			{
				interactionVisual?.Highlight();
			}
		}

		public void DisableVisuals()
		{
			interactionVisual?.Disable();
		}

		public void ResetVisuals()
		{
			interactionVisual?.Idle();
		}

		protected void OnDisable()
		{
			DisableVisuals();
		}

		protected void OnEnable()
		{
			interactionVisual?.Enable();
		}
	}
}
