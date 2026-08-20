using System;
using System.Collections.Generic;
using AstralShift.QTI.Helpers.Attributes;
using Rewired;
using Rewired.Glyphs;
using RewiredConsts;
using UnityEngine;

[RequireComponent(typeof(CanvasGroup))]
public class CustomUnityUIPlayerControllerElementGlyph : CustomUnityUIPlayerControllerElementGlyphBase
{
	[Tooltip("The Player id.")]
	[SerializeField]
	private int _playerId;

	[SerializeField]
	[ActionIdProperty(typeof(RewiredConsts.Action))]
	private int actionToCheck;

	[NonSerialized]
	private int _actionId = -1;

	[NonSerialized]
	private bool _actionIdCached;

	[Tooltip("Animates the glyph image to show when the player has pressed the corresponding action")]
	[SerializeField]
	private bool animated = true;

	[Tooltip("Shows the glyph image has always been in the pressed state")]
	[SerializeField]
	private bool showPressed;

	private float holdTime;

	[Tooltip("Only shows the glyph when the action is pressed")]
	public bool onlyShowWhenButtonPressed;

	[ConditionalHide("onlyShowWhenButtonPressed", true)]
	public float showTime;

	private bool _isVisible;

	private CanvasGroup _canvasGroup;

	private List<CustomGlyphOrTextUI> _glyphs;

	public bool decrementHoldOvertime { get; set; }

	public float decrementSpeedMult { get; set; } = 1f;

	public override int playerId
	{
		get
		{
			return _playerId;
		}
		set
		{
			_playerId = value;
		}
	}

	public override int actionId
	{
		get
		{
			if (!_actionIdCached)
			{
				CacheActionId();
			}
			return _actionId;
		}
		set
		{
			if (ReInput.isReady)
			{
				if (ReInput.mapping.GetAction(value) == null)
				{
					Debug.LogError("Invalid Action id: " + value);
				}
				else
				{
					CacheActionId();
				}
			}
		}
	}

	public void Enable()
	{
		TryAddCanvasGroup();
		_canvasGroup.alpha = 1f;
	}

	public void Disable()
	{
		TryAddCanvasGroup();
		_canvasGroup.alpha = 0f;
	}

	private void TryAddCanvasGroup()
	{
		if (_canvasGroup == null)
		{
			_canvasGroup = GetComponent<CanvasGroup>();
			if (_canvasGroup == null)
			{
				_canvasGroup = base.gameObject.AddComponent<CanvasGroup>();
				_canvasGroup.alpha = 1f;
				_canvasGroup.blocksRaycasts = false;
				_canvasGroup.interactable = false;
			}
		}
	}

	private void CacheActionId()
	{
		if (ReInput.isReady)
		{
			_actionId = actionToCheck;
			_actionIdCached = true;
		}
	}

	protected override bool CreateObjectsAsNeeded(Transform parent, List<GlyphOrTextObject> entries, int count)
	{
		if (count <= 0)
		{
			return false;
		}
		GameObject glyphOrTextPrefabOrDefault = GetGlyphOrTextPrefabOrDefault();
		if ((object)glyphOrTextPrefabOrDefault == null)
		{
			Debug.LogError("Rewired: Default prefab is null.");
			return false;
		}
		if (entries == null)
		{
			return false;
		}
		int count2 = entries.Count;
		if (_glyphs == null)
		{
			_glyphs = new List<CustomGlyphOrTextUI>();
		}
		_glyphs.Clear();
		for (int i = count2; i < count; i++)
		{
			GameObject gameObject = UnityEngine.Object.Instantiate(glyphOrTextPrefabOrDefault);
			gameObject.name = "Object";
			gameObject.hideFlags = HideFlags.DontSave;
			gameObject.transform.SetParent(parent, worldPositionStays: false);
			GlyphOrTextBase component = gameObject.GetComponent<GlyphOrTextBase>();
			if (component == null)
			{
				Debug.LogError("Rewired: Prefab does not contain a " + typeof(GlyphOrTextBase)?.ToString() + " component.");
				UnityEngine.Object.Destroy(gameObject);
				continue;
			}
			GlyphOrTextObject item = new GlyphOrTextObject(component);
			CustomGlyphOrTextUI customGlyphOrTextUI = component as CustomGlyphOrTextUI;
			customGlyphOrTextUI.animate = animated;
			customGlyphOrTextUI.showPressed = showPressed;
			if (decrementHoldOvertime)
			{
				customGlyphOrTextUI.SetHoldWithDecrementOvertime(holdTime, decrementSpeedMult);
			}
			else
			{
				customGlyphOrTextUI.SetHold(holdTime);
			}
			customGlyphOrTextUI.onlyShowWhenButtonPressed = onlyShowWhenButtonPressed;
			customGlyphOrTextUI.showTime = showTime;
			_glyphs.Add(customGlyphOrTextUI);
			entries.Add(item);
			if (entries != base.entries)
			{
				base.entries.Add(item);
			}
		}
		return true;
	}

	public void SetHold(float holdTime)
	{
		this.holdTime = holdTime;
		decrementHoldOvertime = false;
		ClearObjects();
		UpdateGlyphs();
	}

	public void SetHoldWithDecrementOvertime(float holdTime, float decrementSpeedMult = 1f)
	{
		this.holdTime = holdTime;
		this.decrementSpeedMult = decrementSpeedMult;
		decrementHoldOvertime = true;
		ClearObjects();
		UpdateGlyphs();
	}
}
