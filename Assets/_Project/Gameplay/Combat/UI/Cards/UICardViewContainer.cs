using UnityEngine;

namespace AstralShift.HellMaiden.UI.Cards
{
	public class UICardViewContainer : MonoBehaviour
	{
		protected Transform _transform;

		public int ChildAmount => base.transform.childCount;

		public Transform Transform
		{
			get
			{
				if (_transform == null)
				{
					_transform = base.transform;
				}
				return _transform;
			}
		}

		public virtual void ReorderChildren(Transform childToReorder, int targetIndex)
		{
			childToReorder.SetSiblingIndex(targetIndex);
		}

		public virtual void AddEquipmentCard(UICardViewHandler cardViewHandler)
		{
			cardViewHandler.CardView.AssignParent(Transform);
			cardViewHandler.CardView.SetSiblingIndex(cardViewHandler.CardView.SiblingIndex);
		}

		public virtual void AddWeaponCard(UICardViewHandler cardViewHandler)
		{
			cardViewHandler.CardView.AssignParent(Transform);
			cardViewHandler.CardView.transform.SetAsLastSibling();
		}

		public virtual void AddCard(UICardViewHandler cardViewHandler)
		{
			cardViewHandler.CardView.AssignParent(Transform);
			cardViewHandler.CardView.SetSiblingIndex(cardViewHandler.CardView.SiblingIndex);
		}

		public virtual void TempAddCardView(UICardView cardView)
		{
			cardView.Transform.SetParent(Transform);
		}
	}
}
