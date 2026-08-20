using Rewired;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AstralShift.HellMaiden.UI
{
	[RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
	public sealed class UIMouseCursorHandler : UIBehaviour
	{
		public enum CursorType
		{
			Default = 0,
			UI = 1,
			Battle = 2
		}

		public enum CursorState
		{
			Default = 0,
			Clicked = 1
		}

		[Tooltip("Should the hardware pointer be hidden?")]
		[SerializeField]
		private bool _hideHardwarePointer = true;

		[Tooltip("Sets the pointer to the last sibling in the parent hierarchy. Do not enable this on multiple UIPointers under the same parent transform or they will constantly fight each other for dominance.")]
		[SerializeField]
		private bool _autoSort = true;

		public UIMouseCursor battleCursor;

		public UIMouseCursor uICursor;

		private UIMouseCursor _currentCursor;

		private Canvas _canvas;

		private CanvasGroup _canvasGroup;

		private Rewired.Player player;

		public bool autoSort
		{
			get
			{
				return _autoSort;
			}
			set
			{
				if (value != _autoSort)
				{
					_autoSort = value;
					if (value)
					{
						base.transform.SetAsLastSibling();
					}
				}
			}
		}

		protected override void Start()
		{
			base.Start();
			Cursor.visible = false;
			uICursor.Show();
			_currentCursor = uICursor;
			if (_autoSort)
			{
				base.transform.SetAsLastSibling();
			}
			GetDependencies();
		}

		private void Update()
		{
			if (_autoSort && base.transform.GetSiblingIndex() < base.transform.parent.childCount - 1)
			{
				base.transform.SetAsLastSibling();
			}
		}

		public void Show()
		{
			_canvasGroup.alpha = 1f;
			Cursor.visible = false;
		}

		public void Hide()
		{
			_canvasGroup.alpha = 0f;
			Cursor.visible = false;
		}

		protected override void OnTransformParentChanged()
		{
			base.OnTransformParentChanged();
			GetDependencies();
		}

		protected override void OnCanvasGroupChanged()
		{
			base.OnCanvasGroupChanged();
			GetDependencies();
		}

		public void OnScreenPositionChanged(Vector2 screenPosition)
		{
			if (!(_canvas == null) && _canvasGroup.alpha != 0f)
			{
				RectTransformUtility.ScreenPointToLocalPointInRectangle(base.transform.parent as RectTransform, screenPosition, null, out var localPoint);
				base.transform.localPosition = new Vector3(localPoint.x, localPoint.y, base.transform.localPosition.z);
			}
		}

		public void MouseButtonState(int button, bool pressed)
		{
			if ((bool)_currentCursor && button == 4)
			{
				if (pressed)
				{
					_currentCursor.SetState(CursorState.Clicked);
				}
				else
				{
					_currentCursor.SetState(CursorState.Default);
				}
			}
		}

		private void GetDependencies()
		{
			if (!_canvas)
			{
				_canvas = base.transform.root.GetComponentInChildren<Canvas>();
			}
			if (!_canvasGroup)
			{
				_canvasGroup = GetComponent<CanvasGroup>();
			}
		}

		public void SetCursorType(CursorType type)
		{
			uICursor.Hide();
			battleCursor.Hide();
			if (_currentCursor != null)
			{
				_currentCursor.SetState(CursorState.Default);
			}
			switch (type)
			{
			case CursorType.Default:
				uICursor.Show();
				_currentCursor = uICursor;
				break;
			case CursorType.UI:
				uICursor.Show();
				_currentCursor = uICursor;
				break;
			case CursorType.Battle:
				battleCursor.Show();
				_currentCursor = battleCursor;
				break;
			default:
				uICursor.Show();
				_currentCursor = uICursor;
				break;
			}
		}

		public void SetCursorScale(float scaleMultiplier)
		{
			uICursor.SetScale(scaleMultiplier);
			battleCursor.SetScale(scaleMultiplier);
		}

		public void SetCursorColor(float hueValue, float saturationValue)
		{
			uICursor.SetHue(hueValue, saturationValue);
			battleCursor.SetHue(hueValue, saturationValue);
		}
	}
}
