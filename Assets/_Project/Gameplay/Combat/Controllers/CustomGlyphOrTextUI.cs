using System.Collections;
using AstralShift.Control;
using AstralShift.QTI.Helpers.Attributes;
using Rewired;
using Rewired.Glyphs;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CustomGlyphOrTextUI : GlyphOrTextBase<Image, Sprite, TMP_Text>
{
	private PressedGlyphSet.Entry entry;

	[SerializeField]
	private CanvasGroup canvasGroup;

	public int ActionToCheck;

	public Pole axisPole;

	private Player _player;

	private bool _started;

	public bool animate;

	public bool showPressed;

	[SerializeField]
	private Image noHoldShadow;

	[SerializeField]
	private Image holdShadow;

	[SerializeField]
	private Image holdImageBackground;

	[SerializeField]
	private Image holdImage;

	public float holdTime;

	public bool onlyShowWhenButtonPressed;

	[ConditionalHide("onlyShowWhenButtonPressed", true)]
	public float showTime;

	private float showTimer;

	private float _holdTimer;

	private bool _decrementHoldOvertime;

	private float _decrementSpeedMult = 1f;

	private Coroutine _holdFillRoutine;

	private bool _isHolding;

	protected override string textString
	{
		get
		{
			if (!(base.textComponent != null))
			{
				return string.Empty;
			}
			return base.textComponent.text;
		}
		set
		{
			if (!(base.textComponent == null))
			{
				base.textComponent.text = value;
			}
		}
	}

	protected override Sprite glyphGraphic
	{
		get
		{
			if (!(base.glyphComponent != null))
			{
				return null;
			}
			return base.glyphComponent.sprite;
		}
		set
		{
			if (!(base.glyphComponent == null))
			{
				base.glyphComponent.sprite = value;
			}
		}
	}

	private void Start()
	{
		_player = ControllerLifetime.player;
		showTimer = 0f;
		holdImage.fillAmount = 0f;
		_holdTimer = 0f;
		_holdFillRoutine = null;
	}

	public bool IsActive()
	{
		if (base.gameObject.activeInHierarchy)
		{
			return _started;
		}
		return false;
	}

	protected virtual void Update()
	{
		if (!IsActive())
		{
			ResetHoldFill();
			_isHolding = false;
			return;
		}
		bool flag = IsButtonJustPressed();
		bool flag2 = IsButtonJustReleased();
		if (flag)
		{
			_isHolding = true;
		}
		if (flag2)
		{
			_isHolding = false;
		}
		if (onlyShowWhenButtonPressed)
		{
			if (_isHolding)
			{
				showTimer = showTime;
			}
			if (showTimer <= 0f)
			{
				canvasGroup.alpha = 0f;
				ResetHoldFill();
				return;
			}
			canvasGroup.alpha = 1f;
			showTimer -= Time.unscaledDeltaTime;
		}
		if (flag)
		{
			if (animate)
			{
				glyphGraphic = entry.GetPressedSprite();
			}
			StartHoldFill();
		}
		else if (flag2)
		{
			StopHoldFill();
		}
		else if (!_isHolding && !showPressed)
		{
			glyphGraphic = entry.value;
		}
	}

	private void StartHoldFill()
	{
		if (_holdFillRoutine != null)
		{
			StopCoroutine(_holdFillRoutine);
		}
		_holdFillRoutine = StartCoroutine(HoldFillIncreaseRoutine());
	}

	private void StopHoldFill()
	{
		if (_holdFillRoutine != null)
		{
			StopCoroutine(_holdFillRoutine);
		}
		if (_decrementHoldOvertime)
		{
			_holdFillRoutine = StartCoroutine(HoldFillDecreaseRoutine());
		}
		else
		{
			ResetHoldFill();
		}
	}

	private IEnumerator HoldFillIncreaseRoutine()
	{
		while (holdImage.fillAmount < 0.9f)
		{
			_holdTimer += Time.unscaledDeltaTime;
			_holdTimer = Mathf.Clamp(_holdTimer, 0f, holdTime);
			holdImage.fillAmount = _holdTimer / holdTime;
			holdImage.fillAmount = Mathf.Clamp01(holdImage.fillAmount);
			yield return null;
		}
		holdImage.fillAmount = 1f;
	}

	private IEnumerator HoldFillDecreaseRoutine()
	{
		while (holdImage.fillAmount >= 0f)
		{
			_holdTimer -= Time.unscaledDeltaTime * _decrementSpeedMult;
			_holdTimer = Mathf.Clamp(_holdTimer, 0f, holdTime);
			holdImage.fillAmount = _holdTimer / holdTime;
			holdImage.fillAmount = Mathf.Clamp01(holdImage.fillAmount);
			yield return null;
		}
		holdImage.fillAmount = 0f;
	}

	private void ResetHoldFill()
	{
		if (_holdFillRoutine != null)
		{
			StopCoroutine(_holdFillRoutine);
		}
		_holdTimer = 0f;
		holdImage.fillAmount = 0f;
		_holdFillRoutine = null;
	}

	protected virtual bool IsButtonJustPressed()
	{
		if (axisPole == Pole.Positive)
		{
			if (_player.GetButtonDown(ActionToCheck))
			{
				return true;
			}
		}
		else if (axisPole == Pole.Negative && _player.GetNegativeButtonDown(ActionToCheck))
		{
			return true;
		}
		return false;
	}

	protected virtual bool IsButtonJustReleased()
	{
		if (axisPole == Pole.Positive)
		{
			if (_player.GetButtonUp(ActionToCheck))
			{
				return true;
			}
		}
		else if (axisPole == Pole.Negative && _player.GetNegativeButtonUp(ActionToCheck))
		{
			return true;
		}
		return false;
	}

	public void SetHoldWithDecrementOvertime(float holdTime, float decrementSpeedMult = 1f)
	{
		this.holdTime = holdTime;
		ResetHoldTimer();
		_decrementHoldOvertime = true;
		_decrementSpeedMult = decrementSpeedMult;
		if (holdTime == 0f)
		{
			holdImage.gameObject.SetActive(value: false);
			if (holdImageBackground != null)
			{
				holdImageBackground.gameObject.SetActive(value: false);
			}
			if (holdShadow != null)
			{
				holdShadow.gameObject.SetActive(value: false);
			}
			if (noHoldShadow != null)
			{
				noHoldShadow.gameObject.SetActive(value: true);
			}
		}
		else
		{
			holdImage.gameObject.SetActive(value: true);
			if (holdImageBackground != null)
			{
				holdImageBackground.gameObject.SetActive(value: true);
			}
			if (holdShadow != null)
			{
				holdShadow.gameObject.SetActive(value: true);
			}
			if (noHoldShadow != null)
			{
				noHoldShadow.gameObject.SetActive(value: false);
			}
		}
	}

	public void SetHold(float holdTime)
	{
		this.holdTime = holdTime;
		ResetHoldTimer();
		_decrementHoldOvertime = false;
		if (holdTime == 0f)
		{
			holdImage.gameObject.SetActive(value: false);
			if (holdImageBackground != null)
			{
				holdImageBackground.gameObject.SetActive(value: false);
			}
			if (holdShadow != null)
			{
				holdShadow.gameObject.SetActive(value: false);
			}
			if (noHoldShadow != null)
			{
				noHoldShadow.gameObject.SetActive(value: true);
			}
		}
		else
		{
			holdImage.gameObject.SetActive(value: true);
			if (holdImageBackground != null)
			{
				holdImageBackground.gameObject.SetActive(value: true);
			}
			if (holdShadow != null)
			{
				holdShadow.gameObject.SetActive(value: true);
			}
			if (noHoldShadow != null)
			{
				noHoldShadow.gameObject.SetActive(value: false);
			}
		}
	}

	public void ResetHoldTimer()
	{
		ResetHoldFill();
	}

	public override void ShowGlyph(object glyph)
	{
		_started = true;
		entry = glyph as PressedGlyphSet.Entry;
		if (glyph != null && !(glyph is PressedGlyphSet.Entry))
		{
			Debug.LogError("Rewired: Glyph does not implement " + typeof(PressedGlyphSet.Entry).Name + ".");
			return;
		}
		Sprite glyph2 = entry.value;
		if (showPressed)
		{
			glyph2 = entry.GetPressedSprite();
		}
		ShowGlyph(glyph2);
	}

	public new virtual void ShowGlyph(Sprite glyph)
	{
		if (base.glyphComponent == null)
		{
			return;
		}
		if (glyphGraphic != glyph)
		{
			glyphGraphic = glyph;
		}
		if (!base.glyphComponent.gameObject.activeSelf)
		{
			base.glyphComponent.gameObject.SetActive(value: true);
			if (!base.gameObject.activeSelf)
			{
				base.gameObject.SetActive(value: true);
			}
		}
		Hide(TypeFlags.Text);
	}

	protected void Show(TypeFlags flags)
	{
		if (base.glyphComponent != null && (flags & TypeFlags.Glyph) != TypeFlags.None && !base.glyphComponent.gameObject.activeSelf)
		{
			base.glyphComponent.gameObject.SetActive(value: true);
		}
	}
}
