using System.Collections.Generic;
using System.Linq;
using AstralShift.QTI.Helpers.Attributes;
using AstralShift.QTI.Settings;
using AstralShift.QTI.Triggers.Physics;
using UnityEngine;

namespace AstralShift.QTI.Interactors
{
	public class InteractionFinder : MonoBehaviour, IInputInteractor, IInteractor
	{
		protected const int MaxHits = 10;

		public bool showLayers;

		[ConditionalHide("showLayers", true)]
		public LayerMask layer;

		public float radius;

		public float forwardDirection;

		public float height = 2f;

		protected Collider[] _hitResults;

		protected List<InputTrigger> _nearbyInteractions;

		protected InputTrigger _nearestInteraction;

		public int SearchFrameCount = 2;

		protected IInputInteractor iit;

		private void Awake()
		{
			_hitResults = new Collider[10];
			iit = this;
		}

		private void OnValidate()
		{
			InteractionsSettings settings = InteractionsSettings.Instance;
			if (settings != null)
			{
				showLayers = !settings.ForceInputTriggerLayer;
				layer = settings.AssignInputTriggerLayerMask(layer);
			}
		}

		public bool TryInteract()
		{
			if ((bool)_nearestInteraction)
			{
				_nearestInteraction.Interact(this);
			}
			return _nearestInteraction != null;
		}

		public virtual InputTrigger GetInteraction()
		{
			int nearbyInteractions = GetNearbyInteractions();
			if (nearbyInteractions <= 0)
			{
				return null;
			}
			_nearbyInteractions = new List<InputTrigger>();
			for (int i = 0; i < nearbyInteractions; i++)
			{
				if (_hitResults[i].TryGetComponent<InputTrigger>(out var component) && component.CanInteract(iit.GetFacingDirection2D(), iit.GetPosition2D()))
				{
					_nearbyInteractions.Add(component);
					component.ClosestInteractor = this;
				}
			}
			if (_nearbyInteractions.Count == 0)
			{
				return null;
			}
			int maxPriority = _nearbyInteractions.Max((InputTrigger iKT) => iKT.priority.selectedIndex);
			_nearbyInteractions.RemoveAll((InputTrigger inputTrigger) => inputTrigger.priority.selectedIndex < maxPriority);
			float num = float.PositiveInfinity;
			int index = 0;
			for (int num2 = 0; num2 < _nearbyInteractions.Count; num2++)
			{
				float num3 = Vector2.Distance(iit.GetFacingDirection2D() * forwardDirection + iit.GetPosition2D(), _nearbyInteractions[num2].GetPosition2D());
				if (num3 < num)
				{
					num = num3;
					index = num2;
				}
			}
			return _nearbyInteractions[index];
		}

		protected virtual int GetNearbyInteractions()
		{
			return Physics.OverlapCapsuleNonAlloc(base.transform.position, base.transform.position + Vector3.up * height, radius, _hitResults, layer.value);
		}

		private void FixedUpdate()
		{
			if (Time.frameCount % SearchFrameCount == 0)
			{
				InputTrigger interaction = GetInteraction();
				if ((object)interaction == null)
				{
					_nearestInteraction?.ResetVisuals();
					_nearestInteraction = null;
				}
				else if (interaction != null && _nearestInteraction == null)
				{
					_nearestInteraction = interaction;
					_nearestInteraction.HighlightVisuals();
				}
				else if (interaction != _nearestInteraction)
				{
					_nearestInteraction.ResetVisuals();
					_nearestInteraction = interaction;
					_nearestInteraction.HighlightVisuals();
				}
			}
		}

		public Transform GetTransform()
		{
			return base.transform;
		}
	}
}
