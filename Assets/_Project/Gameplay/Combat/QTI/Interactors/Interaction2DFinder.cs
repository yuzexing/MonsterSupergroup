using System.Collections.Generic;
using System.Linq;
using AstralShift.QTI.Helpers.Attributes;
using AstralShift.QTI.Settings;
using AstralShift.QTI.Triggers.Physics2D;
using UnityEngine;

namespace AstralShift.QTI.Interactors
{
	public class Interaction2DFinder : MonoBehaviour, IInput2DInteractor, IInteractor
	{
		protected const int MaxHits = 10;

		public bool showLayers;

		[ConditionalHide("showLayers", true)]
		public LayerMask layer;

		public float radius = 0.5f;

		public float forwardDirection;

		public float height = 2f;

		public Vector3 offset;

		protected Collider2D[] _hitResults;

		protected List<Input2DTrigger> _nearbyInteractions;

		protected Input2DTrigger _nearestInteraction;

		public int SearchFrameCount = 2;

		protected IInput2DInteractor iit;

		private void Awake()
		{
			_hitResults = new Collider2D[10];
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
			height = Mathf.Clamp(height, radius * 2f, float.PositiveInfinity);
		}

		public bool TryInteract()
		{
			if ((bool)_nearestInteraction)
			{
				_nearestInteraction.Interact(this);
			}
			return _nearestInteraction != null;
		}

		public virtual Input2DTrigger GetInteraction()
		{
			int nearbyInteractions = GetNearbyInteractions();
			if (nearbyInteractions <= 0)
			{
				return null;
			}
			_nearbyInteractions = new List<Input2DTrigger>();
			for (int i = 0; i < nearbyInteractions; i++)
			{
				if (_hitResults[i].TryGetComponent<Input2DTrigger>(out var component) && component.CanInteract(iit.GetPosition2D()))
				{
					_nearbyInteractions.Add(component);
					component.ClosestInteractor = this;
				}
			}
			if (_nearbyInteractions.Count == 0)
			{
				return null;
			}
			int maxPriority = _nearbyInteractions.Max((Input2DTrigger iKT) => iKT.priority.selectedIndex);
			_nearbyInteractions.RemoveAll((Input2DTrigger input2DTrigger) => input2DTrigger.priority.selectedIndex < maxPriority);
			float num = float.PositiveInfinity;
			int index = 0;
			for (int num2 = 0; num2 < _nearbyInteractions.Count; num2++)
			{
				float num3 = Vector2.Distance(iit.GetPosition2D(), _nearbyInteractions[num2].GetPosition2D());
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
			return Physics2D.OverlapCapsuleNonAlloc(size: new Vector2(radius * 2f, height), point: base.transform.position + offset, direction: CapsuleDirection2D.Vertical, angle: 0f, results: _hitResults, layerMask: layer.value);
		}

		private void FixedUpdate()
		{
			if (Time.frameCount % SearchFrameCount == 0)
			{
				Input2DTrigger interaction = GetInteraction();
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
