using AstralShift.QTI.Helpers;
using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Triggers.Physics2D
{
	public abstract class Physics2DTrigger : InteractionTrigger
	{
		public LayerMask layerMask;

		public bool useTag;

		[HideInInspector]
		public string targetTag = "Untagged";

		protected Collider2D _collider;

		protected virtual void Reset()
		{
			layerMask = -1;
		}

		public virtual void RefreshCollider()
		{
			if (_collider == null)
			{
				_collider = base.gameObject.GetComponent<Collider2D>();
				if (_collider == null)
				{
					return;
				}
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
