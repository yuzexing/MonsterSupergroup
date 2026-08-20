using System;
using System.Collections;
using System.Collections.Generic;
using AstralShift.Control;
using Rewired;
using Rewired.Glyphs;
using Rewired.Glyphs.UnityUI;
using Unity.VisualScripting;
using UnityEngine;

public abstract class CustomUnityUIPlayerControllerElementGlyphBase : UnityUIControllerElementGlyphBase
{
	[Tooltip("Optional reference to an object that defines options. If blank, the global default options will be used.")]
	[SerializeField]
	private ControllerElementGlyphSelectorOptionsSOBase _options;

	[Tooltip("The range of the Action for which to show glyphs / text. This determines whether to show the glyph for an axis-type Action (ex: Move Horizontal), or the positive/negative pole of an Action (ex: Move Right). For button-type Actions, Full and Positive are equivalent.")]
	[SerializeField]
	private AxisRange _actionRange;

	[Tooltip("Optional parent Transform of the first group of instantiated glyph / text objects. If an axis-type Action is bound to multiple elements, the glyphs bound to the negative pole of the Action will be instantiated under this Transform. This allows you to separate negative and positive groups in order to stack glyph groups horizontally or vertically, for example. If an Action is only bound to one element, the glyph will be instantiated under this transform. If blank, objects will be created as children of this object's Transform.")]
	[SerializeField]
	private Transform _group1;

	[Tooltip("Optional parent Transform of the second group of instantiated glyph / text objects. If an axis-type Action is bound to multiple elements, the glyphs bound to the positive pole of the Action will be instantiated under this Transform. This allows you to separate negative and positive groups in order to stack glyph groups horizontally or vertically, for example. If an Action is only bound to one element, the glyph will be instantiated under group1 instead. If blank, objects will be created as children of either group1 if set or the object's Transform.")]
	[SerializeField]
	private Transform _group2;

	[NonSerialized]
	private List<ActionElementMap> _tempAems = new List<ActionElementMap>();

	[NonSerialized]
	private List<ActionElementMap> _tempCombinedElementAems = new List<ActionElementMap>();

	[NonSerialized]
	private readonly List<GlyphOrTextObject> _group1Objects = new List<GlyphOrTextObject>();

	[NonSerialized]
	private readonly List<GlyphOrTextObject> _group2Objects = new List<GlyphOrTextObject>();

	private ControllerType LastActiveController;

	[NonSerialized]
	private readonly List<object> _tempGlyphs = new List<object>();
	
	public virtual ControllerElementGlyphSelectorOptionsSOBase options
	{
		get
		{
			return _options;
		}
		set
		{
			_options = value;
			RequireRebuild();
		}
	}

	public abstract int playerId { get; set; }

	public abstract int actionId { get; set; }

	public virtual AxisRange actionRange
	{
		get
		{
			return _actionRange;
		}
		set
		{
			_actionRange = value;
		}
	}

	public virtual Transform group1
	{
		get
		{
			return _group1;
		}
		set
		{
			_group1 = value;
			RequireRebuild();
		}
	}

	public virtual Transform group2
	{
		get
		{
			return _group2;
		}
		set
		{
			_group2 = value;
			RequireRebuild();
		}
	}

	protected virtual bool isMousePrioritizedOverKeyboard
	{
		get
		{
			ControllerType controllerType;
			for (int i = 0; TryGetControllerTypeOrder(i, out controllerType); i++)
			{
				switch (controllerType)
				{
				case ControllerType.Mouse:
					return true;
				case ControllerType.Keyboard:
					return false;
				}
			}
			return false;
		}
	}

	protected virtual bool TryGetControllerTypeOrder(int index, out ControllerType controllerType)
	{
		return GetOptionsOrDefault().TryGetControllerTypeOrder(index, out controllerType);
	}

	protected override void Awake()
	{
		ControllerLifetime.OnActionsInputsChanged += UpdateGlyphs;
	}

	protected override void OnDestroy()
	{
		base.OnDestroy();
		ControllerLifetime.OnActionsInputsChanged -= UpdateGlyphs;
	}

	protected override void OnEnable()
	{
		base.OnEnable();
		StartCoroutine(UpdateGlyphsCoroutine());
	}

	private IEnumerator UpdateGlyphsCoroutine()
	{
		yield return null;
		UpdateGlyphs();
	}

	protected void UpdateGlyphs()
	{
		if (ReInput.isReady)
		{
			if (!GlyphTools.TryGetActionElementMaps(playerId, actionId, actionRange, GetOptionsOrDefault(), null, out var aemResult, out var aemResult2))
			{
				Hide();
			}
			else if (aemResult != null && aemResult2 != null)
			{
				ShowSplitAxisBindings(aemResult, aemResult2);
			}
			else if (aemResult != null)
			{
				ShowBinding(aemResult);
			}
			else if (aemResult2 != null)
			{
				ShowBinding(aemResult2);
			}
		}
	}

	protected override void ClearObjects()
	{
		_group1Objects.Clear();
		_group2Objects.Clear();
		base.ClearObjects();
	}

	protected virtual bool ShowBinding(ActionElementMap actionElementMap)
	{
		if (actionElementMap == null)
		{
			return false;
		}
		int num = ShowGlyphsOrText(actionElementMap, GetObjectGroupTransform(0), _group1Objects);
		EvaluateObjectVisibility();
		return num > 0;
	}

	protected override int ShowGlyphsOrText(ActionElementMap actionElementMap, Transform parent, List<GlyphOrTextObject> entries)
	{
		_tempGlyphs.Clear();
		int num = 0;
		if (IsAllowed(AllowedTypes.Glyphs) && ControllerElementGlyphBase.GetGlyphs(actionElementMap, _tempGlyphs) > 0)
		{
			if (!CreateObjectsAsNeeded(parent, entries, _tempGlyphs.Count))
			{
				return 0;
			}
			AssignAxis(actionElementMap, entries);
			for (int i = 0; i < _tempGlyphs.Count; i++)
			{
				entries[i].ShowGlyph(_tempGlyphs[i]);
				_ = entries[i].glyphOrText;
			}
			num += _tempGlyphs.Count;
		}
		else if (IsAllowed(AllowedTypes.Text) && actionElementMap != null)
		{
			if (!CreateObjectsAsNeeded(parent, entries, 1))
			{
				return 0;
			}
			entries[0].ShowText(actionElementMap.elementIdentifierName);
			num++;
		}
		return num;
	}

	protected virtual bool ShowSplitAxisBindings(ActionElementMap negativeAem, ActionElementMap positiveAem)
	{
		if (negativeAem == null && positiveAem == null)
		{
			return false;
		}
		int num = 0;
		if (negativeAem != null && positiveAem != null)
		{
			_tempCombinedElementAems.Clear();
			_tempCombinedElementAems.Add(negativeAem);
			_tempCombinedElementAems.Add(positiveAem);
			num = ShowGlyphsOrText(_tempCombinedElementAems, GetObjectGroupTransform(0), _group1Objects);
		}
		if (num == 0)
		{
			num += ShowGlyphsOrText(negativeAem, GetObjectGroupTransform(0), _group1Objects);
			num += ShowGlyphsOrText(positiveAem, GetObjectGroupTransform(1), _group2Objects);
		}
		EvaluateObjectVisibility();
		return num > 0;
	}

	private void AssignAxis(ActionElementMap actionElementMap, List<GlyphOrTextObject> entries)
	{
		List<CustomGlyphOrTextUI> list = new List<CustomGlyphOrTextUI>();
		for (int i = 0; i < entries.Count; i++)
		{
			list.Add(entries[i].glyphOrText as CustomGlyphOrTextUI);
		}
		int num = 0;
		if (actionElementMap.hasModifiers)
		{
			if (actionElementMap.modifierKey1 != ModifierKey.None)
			{
				list[num].axisPole = Pole.Positive;
				num++;
			}
			if (actionElementMap.modifierKey2 != ModifierKey.None)
			{
				list[num].axisPole = Pole.Positive;
				num++;
			}
			if (actionElementMap.modifierKey3 != ModifierKey.None)
			{
				list[num].axisPole = Pole.Positive;
				num++;
			}
		}
		list[num].ActionToCheck = actionElementMap.actionId;
		list[num].axisPole = actionElementMap.axisContribution;
	}

	protected override void EvaluateObjectVisibility()
	{
		base.EvaluateObjectVisibility();
		Transform objectGroupTransform = GetObjectGroupTransform(0);
		Transform objectGroupTransform2 = GetObjectGroupTransform(1);
		if (objectGroupTransform == objectGroupTransform2)
		{
			EvaluateObjectVisibility(objectGroupTransform);
			return;
		}
		EvaluateObjectVisibility(objectGroupTransform, _group1Objects);
		EvaluateObjectVisibility(objectGroupTransform2, _group2Objects);
	}

	protected virtual int ShowGlyphsOrText(IList<ActionElementMap> bindings, Transform parent, List<GlyphOrTextObject> objects)
	{
		if (bindings == null)
		{
			return 0;
		}
		if (IsAllowed(AllowedTypes.Glyphs) && ActionElementMap.TryGetCombinedElementIdentifierGlyph(bindings, out var result))
		{
			if (!CreateObjectsAsNeeded(parent, objects, 1))
			{
				return 0;
			}
			objects[0].ShowGlyph(result);
			return 1;
		}
		if (IsAllowed(AllowedTypes.Text) && ActionElementMap.TryGetCombinedElementIdentifierName(bindings, out var result2))
		{
			if (!CreateObjectsAsNeeded(parent, objects, 1))
			{
				return 0;
			}
			objects[0].ShowText(result2);
			return 1;
		}
		return 0;
	}

	protected override void Hide()
	{
		base.Hide();
		if (_group1 != null && _group1 != base.transform)
		{
			_group1.gameObject.SetActive(value: false);
		}
		if (_group2 != null && _group2 != base.transform)
		{
			_group2.gameObject.SetActive(value: false);
		}
	}

	protected virtual Transform GetObjectGroupTransform(int groupIndex)
	{
		switch (groupIndex)
		{
		default:
			throw new ArgumentOutOfRangeException();
		case 1:
			if (groupIndex == 1)
			{
				if (_group1 == null)
				{
					return base.transform;
				}
				if (_group2 != null)
				{
					return _group2;
				}
				if (_group1 != null)
				{
					return _group1;
				}
				return base.transform;
			}
			throw new NotImplementedException();
		case 0:
			if (!(_group1 != null))
			{
				return base.transform;
			}
			return _group1;
		}
	}

	protected virtual ControllerElementGlyphSelectorOptions GetOptionsOrDefault()
	{
		if (_options != null && _options.options == null)
		{
			Debug.LogError("Rewired: Options missing on " + typeof(ControllerElementGlyphSelectorOptions).Name + ". Global default options will be used instead.");
			return ControllerElementGlyphSelectorOptions.defaultOptions;
		}
		if (!(_options != null))
		{
			return ControllerElementGlyphSelectorOptions.defaultOptions;
		}
		return _options.options;
	}
}
