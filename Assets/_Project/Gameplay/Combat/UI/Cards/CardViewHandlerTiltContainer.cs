using System.Collections.Generic;
using Com.LuisPedroFonseca.ProCamera2D;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AstralShift.HellMaiden.UI.Cards
{
	public class CardViewHandlerTiltContainer : UIBehaviour
	{
		private readonly List<UICardViewHandler> _viewHandlers = new List<UICardViewHandler>();

		private int _currentChildCount;

		private Transform _transform;

		public Transform Transform
		{
			get
			{
				if (!_transform)
				{
					_transform = base.transform;
				}
				return _transform;
			}
		}

		protected override void OnTransformParentChanged()
		{
			GetCardViewHandlers();
			ApplyTilt();
		}

		protected override void OnRectTransformDimensionsChange()
		{
			GetCardViewHandlers();
			ApplyTilt();
		}

		private void GetCardViewHandlers()
		{
			if (_currentChildCount != Transform.childCount)
			{
				_currentChildCount = Transform.childCount;
				_viewHandlers.Clear();
				for (int i = 0; i < Transform.childCount; i++)
				{
					_viewHandlers.Add(Transform.GetChild(i).GetComponent<UICardViewHandler>());
				}
			}
		}

		private void LateUpdate()
		{
			ApplyTilt();
		}

		private void ApplyTilt()
		{
			if (_viewHandlers.Count != 0)
			{
				for (int i = 0; i < _viewHandlers.Count; i++)
				{
					Vector2 input = new Vector2((((Vector2)ProCamera2D.Instance.GameCamera.ScreenToViewportPoint(_viewHandlers[i].Transform.position)).x * 2f - 1f) * 0.5f, 0f);
					_viewHandlers[i].CardView.ApplyTilt(input, isPosition: false);
					_viewHandlers[i].Transform.localScale = Vector3.one * math.remap(0f, 0.5f, 1f, 0.5f, Mathf.Abs(input.x));
				}
			}
		}
	}
}
