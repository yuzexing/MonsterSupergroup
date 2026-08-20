using System.Collections.Generic;
using AstralShift.HellMaiden.UI.Cards;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AstralShift.HellMaiden.UI.Menus
{
	public class WSMCardViewHandlerContainer : HorizontalCurveLayoutGroup
	{
		[Header("Carousel Settings")]
		[SerializeField]
		protected int elementsCount = 9;

		[SerializeField]
		protected float scrollOffset;

		[SerializeField]
		protected AnimationCurve perspectiveOffsetCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f));

		[SerializeField]
		protected float perspectiveOffsetCurveMultiplier = 1f;

		[SerializeField]
		protected AnimationCurve perspectiveScaleCurve = new AnimationCurve(new Keyframe(0f, 0.1f), new Keyframe(1f, 1f));

		[SerializeField]
		protected float perspectiveScaleCurveMultiplier = 1f;

		[SerializeField]
		protected AnimationCurve perspectiveTiltXCurve = new AnimationCurve(new Keyframe(0f, 0.1f), new Keyframe(1f, 1f));

		[SerializeField]
		protected float perspectiveTiltXCurveMultiplier = 1f;

		[SerializeField]
		protected AnimationCurve perspectiveTiltYCurve = new AnimationCurve(new Keyframe(0f, 0.1f), new Keyframe(1f, 1f));

		[SerializeField]
		protected float perspectiveTiltYCurveMultiplier = 1f;

		private Transform _transform;

		private List<Transform> _childrenTransforms = new List<Transform>();

		private List<float> _childrenNormalizedDistribution = new List<float>();

		private Dictionary<Transform, UICardViewHandler> _transformToCardViewHandlers = new Dictionary<Transform, UICardViewHandler>();

		private Dictionary<Transform, WSMCardSlotViewHandler> _transformToEmptySlotViewHandlers = new Dictionary<Transform, WSMCardSlotViewHandler>();

		public int ElementsCount => elementsCount;

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

		public List<Transform> ChildrenTransforms => _childrenTransforms;

		protected override void OnTransformParentChanged()
		{
			base.OnTransformParentChanged();
			TryGetViewHandlers();
		}

		protected override void OnRectTransformDimensionsChange()
		{
			base.OnRectTransformDimensionsChange();
			TryGetViewHandlers();
		}

		public override void Refresh()
		{
			GetViewHandlers();
			RefreshChildrenSiblingIndexes();
			base.Refresh();
			TryApplyTilt();
		}

		private void GetViewHandlers()
		{
			_childrenTransforms.Clear();
			_transformToCardViewHandlers.Clear();
			_transformToEmptySlotViewHandlers.Clear();
			for (int i = 0; i < Transform.childCount; i++)
			{
				Transform child = Transform.GetChild(i);
				_childrenTransforms.Add(child);
				if (child.TryGetComponent<UICardViewHandler>(out var component))
				{
					_transformToCardViewHandlers.TryAdd(child, component);
				}
				if (child.TryGetComponent<WSMCardSlotViewHandler>(out var component2))
				{
					_transformToEmptySlotViewHandlers.TryAdd(child, component2);
				}
			}
		}

		private bool TryGetViewHandlers()
		{
			if (_childrenTransforms.Count == base.transform.childCount)
			{
				return false;
			}
			_childrenTransforms.Clear();
			_transformToCardViewHandlers.Clear();
			_transformToEmptySlotViewHandlers.Clear();
			for (int i = 0; i < Transform.childCount; i++)
			{
				Transform child = Transform.GetChild(i);
				_childrenTransforms.Add(child);
				if (child.TryGetComponent<UICardViewHandler>(out var component))
				{
					_transformToCardViewHandlers.TryAdd(child, component);
				}
				if (child.TryGetComponent<WSMCardSlotViewHandler>(out var component2))
				{
					_transformToEmptySlotViewHandlers.TryAdd(child, component2);
				}
			}
			return true;
		}

		public void TryGetViewHandlerOfIndex(int index, out UICardViewHandler resultViewHandler, out WSMCardSlotViewHandler resultSlotViewHandler)
		{
			resultViewHandler = null;
			resultSlotViewHandler = null;
			if (index >= 0 && index < _childrenTransforms.Count)
			{
				if (_transformToCardViewHandlers.TryGetValue(_childrenTransforms[index], out resultViewHandler))
				{
					resultSlotViewHandler = null;
				}
				if (_transformToEmptySlotViewHandlers.TryGetValue(_childrenTransforms[index], out resultSlotViewHandler))
				{
					resultViewHandler = null;
				}
			}
		}

		public void TryGetViewHandlerOfTransform(Transform transform, out UICardViewHandler resultViewHandler, out WSMCardSlotViewHandler resultSlotViewHandler)
		{
			if (_transformToCardViewHandlers.TryGetValue(transform, out resultViewHandler))
			{
				resultSlotViewHandler = null;
			}
			if (_transformToEmptySlotViewHandlers.TryGetValue(transform, out resultSlotViewHandler))
			{
				resultViewHandler = null;
			}
		}

		protected void Update()
		{
			if (Application.isPlaying)
			{
				TryApplyTilt();
			}
		}

		private void TryApplyTilt()
		{
			if (_childrenTransforms.Count == 0)
			{
				return;
			}
			for (int i = 0; i < _childrenTransforms.Count; i++)
			{
				if (i <= ElementsCount - 1)
				{
					if (i > _childrenNormalizedDistribution.Count - 1)
					{
						break;
					}
					float f = _childrenNormalizedDistribution[i];
					float time = Mathf.Abs(f);
					float x = Mathf.Sign(f) * perspectiveTiltXCurve.Evaluate(time) * perspectiveTiltXCurveMultiplier;
					float y = perspectiveTiltYCurve.Evaluate(time) * perspectiveTiltYCurveMultiplier;
					Vector2 direction = new Vector2(x, y);
					if (_transformToCardViewHandlers.TryGetValue(_childrenTransforms[i], out var value))
					{
						value.CardView.ApplyRotationOffset(new Vector3(direction.y, 0f - direction.x, 0f));
					}
					if (_transformToEmptySlotViewHandlers.TryGetValue(_childrenTransforms[i], out var value2))
					{
						value2.SlotView.ApplyTilt(direction, direction.magnitude);
					}
				}
			}
		}

		protected override void SetChildrenAlongAxisCustom(int axis, bool isVertical)
		{
			PreCalculateChildrenLocalXPos();
			float num = base.rectTransform.rect.size[axis];
			bool flag = ((axis == 0) ? m_ChildControlWidth : m_ChildControlHeight);
			bool flag2 = ((axis == 0) ? m_ChildScaleWidth : m_ChildScaleHeight);
			bool childForceExpand = ((axis == 0) ? m_ChildForceExpandWidth : m_ChildForceExpandHeight);
			float alignmentOnAxis = GetAlignmentOnAxis(axis);
			bool num2 = isVertical ^ (axis == 1);
			int num3 = (m_ReverseArrangement ? (base.rectChildren.Count - 1) : 0);
			int num4 = ((!m_ReverseArrangement) ? base.rectChildren.Count : 0);
			int num5 = ((!m_ReverseArrangement) ? 1 : (-1));
			if (num2)
			{
				float value = num - (float)((axis == 0) ? base.padding.horizontal : base.padding.vertical);
				for (int i = num3; m_ReverseArrangement ? (i >= num4) : (i < num4); i += num5)
				{
					RectTransform rectTransform = base.rectChildren[i];
					GetChildSizes(rectTransform, axis, flag, childForceExpand, out var min, out var preferred, out var flexible);
					float num6 = (flag2 ? rectTransform.localScale[axis] : 1f);
					float num7 = Mathf.Clamp(value, min, (flexible > 0f) ? num : preferred);
					float startOffset = GetStartOffset(axis, num7 * num6);
					if (i > ElementsCount - 1)
					{
						SetChildAlongAxisWithScale(rectTransform, axis, startOffset, num6);
						continue;
					}
					float num8 = (_childrenNormalizedDistribution[i] + 1f) * 0.5f;
					float num9 = curve.Evaluate(num8) * maxOffset;
					num9 -= maxOffset / 2f;
					if (base.rectChildren.Count == 1)
					{
						num9 = 0f;
					}
					if (flag)
					{
						SetChildAlongAxisWithScale(rectTransform, axis, startOffset - num9, num7, num6);
					}
					else
					{
						float num10 = (num7 - rectTransform.sizeDelta[axis]) * alignmentOnAxis;
						SetChildAlongAxisWithScale(rectTransform, axis, startOffset + num10 - num9, num6);
					}
					if (rotateAlongCurve)
					{
						float num11 = Mathf.Atan(EvaluateCurveSlope(num8, 0.1f)) * 57.29578f;
						SetChildAlongAxisRotation(rectTransform, num11 * angleMultiplier);
					}
					else
					{
						SetChildAlongAxisRotation(rectTransform, 0f);
					}
				}
				return;
			}
			float num12 = (float)((axis == 0) ? base.padding.left : base.padding.top) + scrollOffset;
			float num13 = 0f;
			float num14 = num - GetTotalPreferredSize(axis);
			if (num14 > 0f)
			{
				if (GetTotalFlexibleSize(axis) == 0f)
				{
					num12 = GetStartOffset(axis, GetTotalPreferredSize(axis) - (float)((axis == 0) ? base.padding.horizontal : base.padding.vertical)) + scrollOffset;
				}
				else if (GetTotalFlexibleSize(axis) > 0f)
				{
					num13 = num14 / GetTotalFlexibleSize(axis);
				}
			}
			float t = 0f;
			if (!Mathf.Approximately(GetTotalMinSize(axis), GetTotalPreferredSize(axis)))
			{
				t = Mathf.Clamp01((num - GetTotalMinSize(axis)) / (GetTotalPreferredSize(axis) - GetTotalMinSize(axis)));
			}
			for (int j = num3; m_ReverseArrangement ? (j >= num4) : (j < num4); j += num5)
			{
				RectTransform rectTransform2 = base.rectChildren[j];
				GetChildSizes(rectTransform2, axis, flag, childForceExpand, out var min2, out var preferred2, out var flexible2);
				float num15 = (flag2 ? rectTransform2.localScale[axis] : 1f);
				float num16 = Mathf.Lerp(min2, preferred2, t) + flexible2 * num13;
				if (j > ElementsCount - 1)
				{
					SetChildAlongAxisWithScale(rectTransform2, axis, num12 + 3000f, num15);
					continue;
				}
				float f = _childrenNormalizedDistribution[j];
				if (flag)
				{
					SetChildAlongAxisWithScale(rectTransform2, axis, num12, num16, num15);
				}
				else
				{
					float num17 = (num16 - rectTransform2.sizeDelta[axis]) * alignmentOnAxis;
					if (axis == 0)
					{
						num17 += Mathf.Sign(f) * perspectiveOffsetCurve.Evaluate(Mathf.Abs(f)) * perspectiveOffsetCurveMultiplier;
					}
					SetChildAlongAxisWithScale(rectTransform2, axis, num12 + num17, num15);
				}
				num12 += num16 * num15 + base.spacing;
			}
			for (int k = num3; m_ReverseArrangement ? (k >= num4) : (k < num4); k += num5)
			{
				if (k <= elementsCount - 1)
				{
					RectTransform obj = base.rectChildren[k];
					float time = math.remap(0f, 1f, 1f, 0f, Mathf.Abs(_childrenNormalizedDistribution[k]));
					obj.localScale = Vector3.one * (perspectiveScaleCurve.Evaluate(time) * perspectiveScaleCurveMultiplier);
				}
			}
		}

		private void PreCalculateChildrenLocalXPos()
		{
			_childrenNormalizedDistribution.Clear();
			bool controlSize = m_ChildControlWidth;
			bool childForceExpand = m_ChildForceExpandWidth;
			float alignmentOnAxis = GetAlignmentOnAxis(0);
			float num = base.rectTransform.rect.size[0];
			float num2 = base.rectTransform.sizeDelta.x / 2f;
			float num3 = base.padding.left;
			float num4 = 0f;
			float num5 = num - GetTotalPreferredSize(0);
			int value = (m_ReverseArrangement ? (base.rectChildren.Count - 1) : 0);
			value = Mathf.Clamp(value, 0, ElementsCount - 1);
			int value2 = ((!m_ReverseArrangement) ? base.rectChildren.Count : 0);
			value2 = Mathf.Clamp(value2, 0, ElementsCount);
			int num6 = ((!m_ReverseArrangement) ? 1 : (-1));
			if (num5 > 0f)
			{
				if (GetTotalFlexibleSize(0) == 0f)
				{
					num3 = GetStartOffset(0, GetTotalPreferredSize(0) - (float)base.padding.horizontal);
				}
				else if (GetTotalFlexibleSize(0) > 0f)
				{
					num4 = num5 / GetTotalFlexibleSize(0);
				}
			}
			float t = 0f;
			if (!Mathf.Approximately(GetTotalMinSize(0), GetTotalPreferredSize(0)))
			{
				t = Mathf.Clamp01((num - GetTotalMinSize(0)) / (GetTotalPreferredSize(0) - GetTotalMinSize(0)));
			}
			for (int i = value; m_ReverseArrangement ? (i >= value2) : (i < value2); i += num6)
			{
				RectTransform rectTransform = base.rectChildren[i];
				GetChildSizes(rectTransform, 0, controlSize, childForceExpand, out var min, out var preferred, out var flexible);
				float num7 = Mathf.Lerp(min, preferred, t) + flexible * num4;
				if (i != value)
				{
					RectTransform child = base.rectChildren[i - num6];
					GetChildSizes(child, 0, controlSize, childForceExpand, out var min2, out var preferred2, out var flexible2);
					float num8 = Mathf.Lerp(min2, preferred2, t) + flexible2 * num4;
					num3 += num8 + base.spacing;
				}
				float num9 = (num7 - rectTransform.sizeDelta[0]) * alignmentOnAxis;
				num9 += rectTransform.rect.size[0] / 2f;
				num9 += scrollOffset;
				float num10 = num3 + num9 - num2;
				num10 /= num2;
				_childrenNormalizedDistribution.Add(num10);
			}
		}

		public void ScrollToLeft(bool offsetToEmpty = false)
		{
			TryGetViewHandlers();
			if (offsetToEmpty || !IsLeftSlotEmpty())
			{
				List<Transform> childrenTransforms = _childrenTransforms;
				Transform transform = childrenTransforms[childrenTransforms.Count - 1];
				Transform key = _childrenTransforms[0];
				if (_transformToCardViewHandlers.TryGetValue(transform, out var value))
				{
					value.Hide();
					value.SetSiblingIndex(0);
					value.CardView.SnapTransformToTarget();
				}
				if (_transformToEmptySlotViewHandlers.TryGetValue(transform, out var value2))
				{
					value2.Hide();
					value2.SetSiblingIndex(0);
					value2.SlotView.SnapTransformToTarget();
				}
				if (_transformToCardViewHandlers.TryGetValue(key, out value))
				{
					value.Show();
				}
				if (_transformToEmptySlotViewHandlers.TryGetValue(key, out value2))
				{
					value2.Show();
				}
				_childrenTransforms.Remove(transform);
				_childrenTransforms.Insert(0, transform);
				RefreshChildrenSiblingIndexes();
			}
		}

		public void ScrollToRight(bool offsetToEmpty = false)
		{
			TryGetViewHandlers();
			if (offsetToEmpty || !IsRightSlotEmpty())
			{
				Transform transform = _childrenTransforms[0];
				List<Transform> childrenTransforms = _childrenTransforms;
				Transform key = childrenTransforms[childrenTransforms.Count - 1];
				int count = _childrenTransforms.Count;
				if (_transformToCardViewHandlers.TryGetValue(transform, out var value))
				{
					value.Hide();
					value.SetSiblingIndex(count - 1);
					value.CardView.SnapTransformToTarget();
				}
				if (_transformToEmptySlotViewHandlers.TryGetValue(transform, out var value2))
				{
					value2.Hide();
					value2.SetSiblingIndex(count - 1);
					value2.SlotView.SnapTransformToTarget();
				}
				if (_transformToCardViewHandlers.TryGetValue(key, out value))
				{
					value.Show();
				}
				if (_transformToEmptySlotViewHandlers.TryGetValue(key, out value2))
				{
					value2.Show();
				}
				_childrenTransforms.Remove(transform);
				_childrenTransforms.Add(transform);
				RefreshChildrenSiblingIndexes();
			}
		}

		public void CenterOnTransform(Transform target)
		{
			if (target == null || !_childrenTransforms.Contains(target))
			{
				return;
			}
			int num = elementsCount / 2;
			int count = _childrenTransforms.Count;
			int num2 = 0;
			while (_childrenTransforms.IndexOf(target) != num && num2 < count)
			{
				if (_childrenTransforms.IndexOf(target) > num)
				{
					ScrollToRight(offsetToEmpty: true);
				}
				else
				{
					ScrollToLeft(offsetToEmpty: true);
				}
				num2++;
			}
			RefreshChildrenSiblingIndexes();
			Refresh();
		}

		public void SelectFocusedElement()
		{
			int num = ElementsCount / 2;
			UICardViewHandler value2;
			for (int i = 0; i < _childrenTransforms.Count; i++)
			{
				bool value = i == num;
				if (_transformToCardViewHandlers.TryGetValue(_childrenTransforms[i], out value2))
				{
					if (!value2)
					{
						_transformToCardViewHandlers.Remove(_childrenTransforms[i]);
						continue;
					}
					value2.AllowInteraction(value);
					value2.UnSelect();
					value2.CardView.UnHover();
					value2.CardView.DisableTilt();
					value2.CardView.EnableSelectionOuterGlow(state: false);
				}
				if (_transformToEmptySlotViewHandlers.TryGetValue(_childrenTransforms[i], out var value3))
				{
					if (!value3)
					{
						_transformToEmptySlotViewHandlers.Remove(_childrenTransforms[i]);
					}
					else
					{
						value3.AllowInteraction(value);
					}
				}
			}
			if (num < _childrenTransforms.Count && _transformToCardViewHandlers.TryGetValue(_childrenTransforms[num], out value2))
			{
				EventSystem.current.SetSelectedGameObject(_childrenTransforms[num].gameObject);
				value2.Select();
				value2.CardView.Hover();
				value2.CardView.EnableTilt();
				value2.CardView.EnableSelectionOuterGlow(state: true);
				return;
			}
			for (int j = 0; j < _childrenTransforms.Count; j++)
			{
				ScrollToRight(offsetToEmpty: true);
				if (num < _childrenTransforms.Count && _transformToCardViewHandlers.TryGetValue(_childrenTransforms[num], out value2))
				{
					EventSystem.current.SetSelectedGameObject(_childrenTransforms[num].gameObject);
					value2.Select();
					value2.CardView.Hover();
					value2.CardView.EnableTilt();
					value2.CardView.EnableSelectionOuterGlow(state: true);
					break;
				}
			}
		}

		private void RefreshChildrenSiblingIndexes()
		{
			for (int i = 0; i < _childrenTransforms.Count; i++)
			{
				TryGetViewHandlerOfIndex(i, out var resultViewHandler, out var resultSlotViewHandler);
				if ((bool)resultViewHandler)
				{
					resultViewHandler.SetSiblingIndex(i);
				}
				if ((bool)resultSlotViewHandler)
				{
					resultSlotViewHandler.SetSiblingIndex(i);
				}
			}
		}

		public bool IsRightSlotEmpty()
		{
			int num = ElementsCount / 2 + 1;
			if (num >= _childrenTransforms.Count || num < 0)
			{
				return true;
			}
			return !_transformToCardViewHandlers.ContainsKey(_childrenTransforms[num]);
		}

		public bool IsLeftSlotEmpty()
		{
			int num = ElementsCount / 2 - 1;
			if (num < 0 || num >= _childrenTransforms.Count)
			{
				return true;
			}
			return !_transformToCardViewHandlers.ContainsKey(_childrenTransforms[num]);
		}

		public bool TryGetFocusedCard(out UICardViewHandler cardViewHandler)
		{
			int num = ElementsCount / 2;
			if (num >= 0 && num < _childrenTransforms.Count)
			{
				return _transformToCardViewHandlers.TryGetValue(_childrenTransforms[num], out cardViewHandler);
			}
			cardViewHandler = null;
			return false;
		}
	}
}
