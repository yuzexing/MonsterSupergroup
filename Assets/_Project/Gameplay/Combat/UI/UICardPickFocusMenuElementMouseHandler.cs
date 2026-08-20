using System.Collections.Generic;
using AstralShift.HellMaiden.UI.Menus;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AstralShift.HellMaiden.UI
{
	[RequireComponent(typeof(UIFocusParentSwitcherElement))]
	public class UICardPickFocusMenuElementMouseHandler : UIBehaviour, IPointerEnterHandler, IEventSystemHandler
	{
		[SerializeField]
		[HideInInspector]
		private UIFocusParentSwitcherElement _focusElement;

		private readonly List<CanvasGroup> m_CanvasGroupCache = new List<CanvasGroup>();

		private bool _groupsAllowInteraction = true;

		public bool IsInteractable => _groupsAllowInteraction;

		protected override void OnCanvasGroupChanged()
		{
			bool groupsAllowInteraction = ParentGroupAllowsInteraction();
			_groupsAllowInteraction = groupsAllowInteraction;
		}

		private bool ParentGroupAllowsInteraction()
		{
			Transform parent = base.transform;
			while (parent != null)
			{
				parent.GetComponents(m_CanvasGroupCache);
				for (int i = 0; i < m_CanvasGroupCache.Count; i++)
				{
					if (m_CanvasGroupCache[i].enabled && !m_CanvasGroupCache[i].interactable)
					{
						return false;
					}
					if (m_CanvasGroupCache[i].ignoreParentGroups)
					{
						return true;
					}
				}
				parent = parent.parent;
			}
			return true;
		}

		protected override void Awake()
		{
			if (_focusElement == null)
			{
				_focusElement = GetComponent<UIFocusParentSwitcherElement>();
			}
		}

		public virtual void OnPointerEnter(PointerEventData eventData)
		{
			if (IsInteractable && !_focusElement.permanentFocus)
			{
				UICardPickMenuView.Instance.SwitchFocusGroup(_focusElement);
			}
		}
	}
}
