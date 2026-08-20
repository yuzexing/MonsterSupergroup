using UnityEngine;
using UnityEngine.UI;

public class HorizontalCurveLayoutGroup : HorizontalLayoutGroup
{
	[SerializeField]
	protected bool autoUpdate = true;

	[SerializeField]
	protected float maxOffset;

	[SerializeField]
	protected AnimationCurve curve;

	[SerializeField]
	protected bool rotateAlongCurve;

	[SerializeField]
	protected float angleMultiplier = 1f;

	[SerializeField]
	protected bool isFrozen;

	public float AngleMultiplier
	{
		get
		{
			return angleMultiplier;
		}
		set
		{
			angleMultiplier = value;
			SetLayoutVertical();
		}
	}

	public float MaxOffset
	{
		get
		{
			return maxOffset;
		}
		set
		{
			maxOffset = value;
			SetLayoutVertical();
		}
	}

	public AnimationCurve Curve
	{
		get
		{
			return curve;
		}
		set
		{
			curve = value;
			SetLayoutVertical();
		}
	}

	public bool IsFrozen
	{
		get
		{
			return isFrozen;
		}
		set
		{
			isFrozen = value;
		}
	}

	public override void SetLayoutHorizontal()
	{
		if (!isFrozen)
		{
			SetChildrenAlongAxisCustom(0, isVertical: false);
		}
	}

	public override void SetLayoutVertical()
	{
		if (!isFrozen)
		{
			SetChildrenAlongAxisCustom(1, isVertical: false);
		}
	}

	public override void CalculateLayoutInputHorizontal()
	{
		if (!isFrozen && autoUpdate)
		{
			base.CalculateLayoutInputHorizontal();
		}
	}

	public override void CalculateLayoutInputVertical()
	{
		if (!isFrozen && autoUpdate)
		{
			base.CalculateLayoutInputVertical();
		}
	}

	public void ForceCalculateLayoutInput()
	{
		base.CalculateLayoutInputHorizontal();
		base.CalculateLayoutInputVertical();
	}

	public void Freeze(bool state)
	{
		isFrozen = state;
		if (!state)
		{
			Refresh();
		}
	}

	public virtual void Refresh()
	{
		ForceCalculateLayoutInput();
		SetLayoutHorizontal();
		SetLayoutVertical();
	}

	protected virtual void SetChildrenAlongAxisCustom(int axis, bool isVertical)
	{
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
				float curveSampleTimeByIndex = GetCurveSampleTimeByIndex(i, base.rectChildren.Count);
				float num8 = curve.Evaluate(curveSampleTimeByIndex) * maxOffset;
				num8 -= maxOffset / 2f;
				if (base.rectChildren.Count == 1)
				{
					num8 = 0f;
				}
				if (flag)
				{
					SetChildAlongAxisWithScale(rectTransform, axis, startOffset - num8, num7, num6);
				}
				else
				{
					float num9 = (num7 - rectTransform.sizeDelta[axis]) * alignmentOnAxis;
					SetChildAlongAxisWithScale(rectTransform, axis, startOffset + num9 - num8, num6);
				}
				if (rotateAlongCurve)
				{
					float num10 = Mathf.Atan(EvaluateCurveSlope(curveSampleTimeByIndex)) * 57.29578f;
					SetChildAlongAxisRotation(rectTransform, num10 * angleMultiplier);
				}
				else
				{
					SetChildAlongAxisRotation(rectTransform, 0f);
				}
			}
			return;
		}
		float num11 = ((axis == 0) ? base.padding.left : base.padding.top);
		float num12 = 0f;
		float num13 = num - GetTotalPreferredSize(axis);
		if (num13 > 0f)
		{
			if (GetTotalFlexibleSize(axis) == 0f)
			{
				num11 = GetStartOffset(axis, GetTotalPreferredSize(axis) - (float)((axis == 0) ? base.padding.horizontal : base.padding.vertical));
			}
			else if (GetTotalFlexibleSize(axis) > 0f)
			{
				num12 = num13 / GetTotalFlexibleSize(axis);
			}
		}
		float t = 0f;
		if (GetTotalMinSize(axis) != GetTotalPreferredSize(axis))
		{
			t = Mathf.Clamp01((num - GetTotalMinSize(axis)) / (GetTotalPreferredSize(axis) - GetTotalMinSize(axis)));
		}
		for (int j = num3; m_ReverseArrangement ? (j >= num4) : (j < num4); j += num5)
		{
			RectTransform rectTransform2 = base.rectChildren[j];
			GetChildSizes(rectTransform2, axis, flag, childForceExpand, out var min2, out var preferred2, out var flexible2);
			float num14 = (flag2 ? rectTransform2.localScale[axis] : 1f);
			float num15 = Mathf.Lerp(min2, preferred2, t);
			num15 += flexible2 * num12;
			if (flag)
			{
				SetChildAlongAxisWithScale(rectTransform2, axis, num11, num15, num14);
			}
			else
			{
				float num16 = (num15 - rectTransform2.sizeDelta[axis]) * alignmentOnAxis;
				SetChildAlongAxisWithScale(rectTransform2, axis, num11 + num16, num14);
			}
			num11 += num15 * num14 + base.spacing;
		}
	}

	protected virtual void SetChildAlongAxisRotation(RectTransform rect, float angle)
	{
		if (!(rect == null))
		{
			m_Tracker.Add(this, rect, DrivenTransformProperties.Rotation);
			Vector3 localEulerAngles = rect.localEulerAngles;
			localEulerAngles = new Vector3(localEulerAngles.x, localEulerAngles.y, angle);
			rect.localEulerAngles = localEulerAngles;
		}
	}

	protected void GetChildSizes(RectTransform child, int axis, bool controlSize, bool childForceExpand, out float min, out float preferred, out float flexible)
	{
		if (!controlSize)
		{
			min = child.sizeDelta[axis];
			preferred = min;
			flexible = 0f;
		}
		else
		{
			min = LayoutUtility.GetMinSize(child, axis);
			preferred = LayoutUtility.GetPreferredSize(child, axis);
			flexible = LayoutUtility.GetFlexibleSize(child, axis);
		}
		if (childForceExpand)
		{
			flexible = Mathf.Max(flexible, 1f);
		}
	}

	protected float GetCurveSampleTimeByIndex(int index, int count)
	{
		if (count <= 1)
		{
			return 0.5f;
		}
		return (float)index / (float)(count - 1);
	}

	protected float EvaluateCurveSlope(float t, float delta = 0.01f)
	{
		float num = Mathf.Max(0f, t - delta);
		float num2 = Mathf.Min(1f, t + delta);
		float num3 = curve.Evaluate(num);
		float num4 = curve.Evaluate(num2);
		float num5 = num2 - num;
		if (num5 == 0f)
		{
			return 0f;
		}
		return (num4 - num3) / num5;
	}
}
