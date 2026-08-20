using AstralShift.QTI.Helpers;
using AstralShift.QTI.Interactions.Visuals;
using AstralShift.QTI.Interactors;
using AstralShift.QTI.Settings;
using UnityEngine;

namespace AstralShift.QTI.Triggers.Physics2D
{
	[AddComponentMenu("QTI/Triggers/Physics2D/Input2DTrigger")]
	public class Input2DTrigger : Physics2DTrigger
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

		public IInput2DInteractor ClosestInteractor { get; set; }

		public override void Interact(IInteractor interactor)
		{
			if (interaction == null)
			{
				Debug.LogError("No Interaction assigned to Trigger!!");
			}
			else if (base.enabled && (bool)interaction && interaction.enabled)
			{
				if (CanInteract(GetPosition2D()))
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

		public virtual bool CanInteract(Vector2 position)
		{
			if (!interaction.enabled || !interaction.CanInteract())
			{
				ResetVisuals();
				return false;
			}
			Vector2 directionAtoB = Math.GetDirectionAtoB(position, GetPosition2D());
			bool flag;
			if (isFixedAngle)
			{
				_forwardDirection = Quaternion.AngleAxis(interactionDirection, base.transform.forward) * GetFacingDirection2D();
				_currentFacingAngle = Vector2.Angle(_forwardDirection, directionAtoB);
				_currentRelativeAngle = Vector2.Angle(_forwardDirection, directionAtoB);
				flag = _currentFacingAngle <= interactionAngle / 2f && _currentRelativeAngle <= interactionAngle / 2f;
			}
			else
			{
				_currentFacingAngle = Vector2.Angle(directionAtoB, directionAtoB);
				flag = _currentFacingAngle <= interactionAngle / 2f;
			}
			if (!flag)
			{
				ResetVisuals();
			}
			return flag;
		}

		public override Vector3 GetFacingDirection()
		{
			return base.transform.up;
		}

		public override Vector2 GetFacingDirection2D()
		{
			return base.transform.up;
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
