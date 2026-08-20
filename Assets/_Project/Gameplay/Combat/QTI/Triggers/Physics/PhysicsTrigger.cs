using AstralShift.QTI.Helpers;
using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Triggers.Physics
{
	public abstract class PhysicsTrigger : InteractionTrigger
	{
		public LayerMask layerMask;

		public bool useTag;

		[HideInInspector]
		public string targetTag = "Untagged";

		protected Collider _collider;

		protected virtual void Reset()
		{
			layerMask = -1;
		}

		public virtual void RefreshCollider()
		{
			if (_collider == null)
			{
				_collider = base.gameObject.GetComponent<Collider>();
				if (_collider == null)
				{
					return;
				}
			}
			if (_collider is MeshCollider meshCollider)
			{
				meshCollider.convex = true;
			}
			_collider.isTrigger = true;
		}

		protected bool FilterInteractor(GameObject go, out IInteractor interactor)
		{
			interactor = null;
			if (!base.enabled || !PhysicsHelper.ContainsLayer(go.layer, layerMask) || (useTag && !go.CompareTag(targetTag)) || !go.TryGetComponent<IInteractor>(out interactor))
			{
				return false;
			}
			return true;
		}
	}
}
