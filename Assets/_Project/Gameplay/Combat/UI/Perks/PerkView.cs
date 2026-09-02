using System;
using System.Text;
using Animancer;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Data.Perks;
using AstralShift.HellMaiden.UI.Cards;
using AstralShift.Helpers;
using AstralShift.UI;
using Coffee.UIEffects;
using Coffee.UIExtensions;
using Com.LuisPedroFonseca.ProCamera2D;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using FMODUnity;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI.Perks
{
	public class PerkView : CustomUIButton, IPointerMoveHandler, IEventSystemHandler
	{
		[Header("References")]
		[SerializeField]
		private RawImage perkViewport;

		[SerializeField]
		private Transform shakeParent;

		[Space]
		[Header("Title / Description / Stats")]
		[SerializeField]
		private TextMeshProUGUI perkTitleText;

		[SerializeField]
		private TextMeshProUGUI description;

		[SerializeField]
		private CanvasGroup descriptionCanvasGroup;

		[SerializeField]
		private VerticalLayoutGroup descriptionLayoutGroup;

		[SerializeField]
		private float selectedDescriptionAlpha = 1f;

		[SerializeField]
		private float deselectedDescriptionAlpha = 0.5f;

		[SerializeField]
		private RectTransform descriptionPivot;

		[SerializeField]
		private Vector2 descriptionPivotDefaultPosition = new Vector2(0f, -150f);

		[SerializeField]
		private TextMeshProUGUI perkStatsChangeText;

		[Space]
		[Header("Level")]
		[SerializeField]
		private TextMeshProUGUI levelNumberText;

		[SerializeField]
		private Image levelBubbleImage;

		[SerializeField]
		private Sprite defaultBubbleSprite;

		[SerializeField]
		private Sprite crystalBubbleSprite;

		[SerializeField]
		private float levelBubbleSaturation;

		[SerializeField]
		private float levelBubbleBrightness;

		[Space]
		[Header("Animations")]
		[SerializeField]
		protected AnimancerComponent animancer;

		[SerializeField]
		protected ClipTransition openAnimation;

		[SerializeField]
		protected ClipTransition closeAnimation;

		[SerializeField]
		protected ClipTransition selectedAnimation;

		[SerializeField]
		private PerkAnimationSettings animationSettings;

		[Space]
		[Header("Outer Glow VFX")]
		[SerializeField]
		private RawImage outerGlowEffect;

		[SerializeField]
		private UIEffect outerGlowUIEffect;

		[Space]
		[Header("Glow VFX")]
		private GameObject glowEffect;

		[SerializeField]
		private Transform glowPosition;

		[SerializeField]
		private GameObject bronzeGlowEffect;

		[SerializeField]
		private GameObject silverGlowEffect;

		[SerializeField]
		private GameObject goldGlowEffect;

		[SerializeField]
		private GameObject crystalGlowEffect;

		[Space]
		[Header("Place VFX")]
		[SerializeField]
		private UIParticle equipParticleSystem;

		[Space]
		[Header("Sounds")]
		[SerializeField]
		private EventReference bronzePerkSelectedSound;

		[SerializeField]
		private EventReference silverPerkSelectedSound;

		[SerializeField]
		private EventReference goldPerkSelectedSound;

		[SerializeField]
		private EventReference crystalPerkSelectedSound;

		private RuntimePerkData _perkData;

		public Action onPerkSelectedTweenStart;

		public new Action<PerkPoolID, RuntimePerkData> OnSubmit;

		private AnimancerState _perkSelectedAnimationState;

		private AnimancerState _openCloseAnimationState;

		private Transform _transform;

		private bool _canTilt;

		private Transform _viewPortPositionReference;

		private const string StatChangeFormat = "{0}{1}: {2}% ▶ <color=#{3}>{4}%</color>";

		private const string ModifierNameI2Prefix = "STP_";

		private Tween _idleOffsetTween;

		private bool _allowIdleFloat = true;

		private float _idleVerticalPositionOffset;

		private Vector3 _idleRotationOffset;

		private Vector3 _currentRotationOffset;

		private Tween _hoverRotateTween;

		private Tween _hoverScaleTween;

		private Sequence onShowSequence;

		private Sequence onHideSequence;

		private Sequence perkSelectedSequence;

		public RuntimePerkData PerkData => _perkData;

		public Perk3DView Perk3DView => UIPerkRenderingManager.Instance.GetPerk3DView(this);

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

		private new void Awake()
		{
			outerGlowEffect.color = new Color(1f, 1f, 1f, 0f);
			onSubmit.RemoveAllListeners();
			onSubmit.AddListener(ChosePerk);
			descriptionCanvasGroup.alpha = 0f;
		}

		public void Initialize(RuntimePerkData perkData)
		{
			_perkData = perkData;
			if (_perkData.Rarity == PerkRarity.Crystal)
			{
				_viewPortPositionReference = shakeParent;
			}
			else
			{
				_viewPortPositionReference = Transform;
			}
		}

		private async void ChosePerk()
		{
			try
			{
				EventReference perkSelectedSound = GetPerkSelectedSound(_perkData.Rarity);
				if (!perkSelectedSound.IsNull)
				{
					RuntimeManager.PlayOneShot(perkSelectedSound);
				}
				base.interactable = false;
				await PerkSelectedTween();
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		public void BindDynamicTexture()
		{
			perkViewport.texture = UIPerkRenderingManager.Instance.GetDynamicTexture(this);
		}

		private void LateUpdate()
		{
			if (Application.isPlaying)
			{
				ApplyIdleAnimation();
				ApplyViewPortPositionTo3DView();
			}
		}

		public void SetTitle(string title)
		{
			perkTitleText.text = title;
		}

		public void SetDescription(string description)
		{
			this.description.text = description;
		}

		public void SetDescriptionOffset(Vector2 offset)
		{
			descriptionPivot.anchoredPosition = descriptionPivotDefaultPosition + offset;
		}

		public void SetLevel(int level)
		{
			if (_perkData.Rarity == PerkRarity.Crystal)
			{
				levelNumberText.text = "S";
				levelBubbleImage.color = Color.white;
				levelBubbleImage.sprite = crystalBubbleSprite;
			}
			else
			{
				levelNumberText.text = level.ToString();
				levelBubbleImage.color = Color.HSVToRGB(GetLevelHue(level), levelBubbleSaturation, levelBubbleBrightness);
				levelBubbleImage.sprite = defaultBubbleSprite;
			}
		}

		private float GetLevelHue(int level)
		{
			int num = 2;
			int num2 = AstralShift.HellMaiden.Data.Perks.PerkData.LevelsPerRarity * (num + 1);
			return Mathf.Clamp01(((float)((int)PerkData.Data.GetLowestRarity() * AstralShift.HellMaiden.Data.Perks.PerkData.LevelsPerRarity + level) - 1f) / ((float)num2 - 1f)) * 0.85f;
		}

		public void SetIcon(Sprite icon)
		{
			Perk3DView.SetIcon(icon);
		}

		public void SetGlowRarity(PerkRarity rarity)
		{
			switch (rarity)
			{
			case PerkRarity.Bronze:
				glowEffect = UnityEngine.Object.Instantiate(bronzeGlowEffect, glowPosition);
				break;
			case PerkRarity.Silver:
				glowEffect = UnityEngine.Object.Instantiate(silverGlowEffect, glowPosition);
				break;
			case PerkRarity.Gold:
				glowEffect = UnityEngine.Object.Instantiate(goldGlowEffect, glowPosition);
				break;
			case PerkRarity.Crystal:
				glowEffect = UnityEngine.Object.Instantiate(crystalGlowEffect, glowPosition);
				break;
			default:
				glowEffect = UnityEngine.Object.Instantiate(bronzeGlowEffect, glowPosition);
				break;
			}
			glowEffect.transform.localPosition = Vector3.zero;
		}

		public void SetStatChangeInfo(RuntimePerkData runtimePerkData, RuntimePerk currentPerk)
		{
			StringBuilder stringBuilder = new StringBuilder();
			PerkRarityModifiersData rarity = runtimePerkData.Data.GetRarity(runtimePerkData.Rarity);
			for (int i = 0; i < rarity.Modifiers.Length; i++)
			{
				PerkModifierApplication perkDataModifier = rarity.Modifiers[i];
				float parameterByIndex = perkDataModifier.GetParameterByIndex(0);
				float num = 0f;
				if (currentPerk != null)
				{
					num = currentPerk.GetAtIndexModifierParameterValue(i);
				}
				Color color = Color.green;
				if (num + parameterByIndex < num)
				{
					color = Color.red;
				}
				string term = "STP_" + ModifiersStringHelpers.GetPerkModifierNameLocKey(perkDataModifier.ModifierIdValue);
				LocalizationMediator.GetTranslation(ref term);
				stringBuilder.AppendFormat("{0}{1}: {2}% ▶ <color=#{3}>{4}%</color>", ModifiersStringHelpers.GetPerkModifierStringIcon(perkDataModifier.ModifierIdValue), term, DataModifierUtils.FormatMultiplierToPercentage(num) ?? "", ColorUtility.ToHtmlStringRGBA(color), " " + DataModifierUtils.FormatMultiplierToPercentage(num + parameterByIndex));
				if (i + 1 < rarity.Modifiers.Length)
				{
					stringBuilder.Append("\n");
				}
			}
			perkStatsChangeText.text = stringBuilder.ToString();
		}

		public async UniTask RefreshLayout()
		{
			LayoutRebuilder.ForceRebuildLayoutImmediate(descriptionLayoutGroup.transform as RectTransform);
			await UniTask.NextFrame();
		}

		private EventReference GetPerkSelectedSound(PerkRarity rarity)
		{
			return rarity switch
			{
				PerkRarity.Bronze => bronzePerkSelectedSound, 
				PerkRarity.Silver => silverPerkSelectedSound, 
				PerkRarity.Gold => goldPerkSelectedSound, 
				PerkRarity.Crystal => crystalPerkSelectedSound, 
				_ => bronzePerkSelectedSound, 
			};
		}

		public void EnableIdleAnimation(bool state)
		{
			_allowIdleFloat = state;
			if (!_idleOffsetTween.IsActive())
			{
				InitIdleAnimation();
			}
		}

		private void InitIdleAnimation()
		{
			float resAdjustedScreenSpaceOffset = UIResolutionHelpers.GetResAdjustedScreenSpaceOffset(animationSettings.IdleMoveOffset * (float)((Transform.GetSiblingIndex() % 2 != 0) ? 1 : (-1)));
			_idleOffsetTween = DOTween.To(() => _idleVerticalPositionOffset, delegate(float value)
			{
				_idleVerticalPositionOffset = value;
			}, resAdjustedScreenSpaceOffset, animationSettings.IdleMoveTime).SetEase(animationSettings.IdleMoveOffsetEase.GetEaseFunction()).SetLoops(-1, LoopType.Yoyo)
				.SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
		}

		private void ApplyIdleAnimation()
		{
			if ((bool)Perk3DView && _allowIdleFloat)
			{
				float num = (_allowIdleFloat ? _idleVerticalPositionOffset : 0f);
				shakeParent.localPosition = Vector3.Lerp(shakeParent.localPosition, Vector3.up * num, Time.unscaledDeltaTime);
				_currentRotationOffset = Vector3.Lerp(_currentRotationOffset, _allowIdleFloat ? _idleRotationOffset : Vector3.zero, Time.unscaledDeltaTime);
				Perk3DView.ApplyRotationOffset(_currentRotationOffset);
			}
		}

		public void SetRotationOffset(Vector3 offset)
		{
			_idleRotationOffset = offset;
			_currentRotationOffset = _idleRotationOffset;
		}

		private void ApplyViewPortPositionTo3DView()
		{
			if ((bool)Perk3DView)
			{
				Vector2 viewPortPosition = ProCamera2D.Instance.GameCamera.ScreenToViewportPoint(_viewPortPositionReference.position);
				Perk3DView.SetViewPortPosition(viewPortPosition);
			}
		}

		public void Hover()
		{
			float hoverPunchAngle = animationSettings.HoverPunchAngle;
			float hoverRotationTime = animationSettings.HoverRotationTime;
			int hoverVibration = animationSettings.HoverVibration;
			float hoverElasticity = animationSettings.HoverElasticity;
			float hoverScaleTime = animationSettings.HoverScaleTime;
			float hoverScaleMultiplier = animationSettings.HoverScaleMultiplier;
			_hoverRotateTween?.Kill(complete: true);
			_hoverScaleTween?.Kill();
			_hoverRotateTween = shakeParent.DOPunchRotation(Vector3.forward * hoverPunchAngle, hoverRotationTime, hoverVibration, hoverElasticity).SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
			_hoverScaleTween = shakeParent.DOScale(hoverScaleMultiplier, hoverScaleTime).SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
			_hoverRotateTween.Play();
			_hoverScaleTween.Play();
		}

		public void UnHover()
		{
			float hoverScaleTime = animationSettings.HoverScaleTime;
			_hoverRotateTween?.Kill(complete: true);
			_hoverScaleTween?.Kill();
			_hoverScaleTween = shakeParent.DOScale(Vector3.one, hoverScaleTime).SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
			_hoverScaleTween.Play();
		}

		public void EnableOuterGlow(bool state)
		{
			float hoverGlowFadeTime = animationSettings.HoverGlowFadeTime;
			Color hoverGlowColor = animationSettings.HoverGlowColor;
			if (outerGlowEffect.texture == null)
			{
				outerGlowEffect.texture = UIPerkRenderingManager.Instance.GetDynamicTexture(this);
			}
			outerGlowEffect.DOFade(state ? 1 : 0, hoverGlowFadeTime).SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
			DOTween.To(() => outerGlowUIEffect.color, delegate(Color value)
			{
				outerGlowUIEffect.color = value;
			}, hoverGlowColor, hoverGlowFadeTime).SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
		}

		public void EnableTilt()
		{
			_canTilt = true;
		}

		public void StopTilt()
		{
			Perk3DView.StopTilt(animationSettings.HoverTiltStopSpeed);
		}

		public void DisableTilt(bool instant = false)
		{
			_canTilt = false;
			if (instant)
			{
				Perk3DView.StopTiltInstant();
			}
			else
			{
				Perk3DView.StopTilt(animationSettings.HoverTiltStopSpeed);
			}
		}

		public void ApplyTilt(Vector2 input, bool isPosition = true, bool accumulateRotation = false)
		{
			if (_canTilt && (bool)Perk3DView)
			{
				float hoverTiltAmount = animationSettings.HoverTiltAmount;
				float hoverTiltSpeed = animationSettings.HoverTiltSpeed;
				Vector2 direction = input;
				if (isPosition)
				{
					Vector2 vector = ProCamera2D.Instance.GameCamera.ScreenToViewportPoint(Transform.position);
					direction = (Vector2)ProCamera2D.Instance.GameCamera.ScreenToViewportPoint(input) - vector;
				}
				Perk3DView.ApplyTilt(direction, hoverTiltAmount, hoverTiltSpeed);
			}
		}

		public override void OnPointerEnter(PointerEventData eventData)
		{
			if (base.interactable)
			{
				base.OnPointerEnter(eventData);
				Select();
			}
		}

		public override void OnPointerExit(PointerEventData eventData)
		{
			if (base.interactable)
			{
				base.OnPointerExit(eventData);
				Deselect();
			}
		}

		public void OnPointerMove(PointerEventData eventData)
		{
			if (base.interactable)
			{
				ApplyTilt(Input.mousePosition);
			}
		}

		public override void OnSelect(BaseEventData eventData)
		{
			if (base.interactable)
			{
				base.OnSelect(eventData);
				Select();
			}
		}

		public override void OnDeselect(BaseEventData eventData)
		{
			if (base.interactable)
			{
				base.OnDeselect(eventData);
				Deselect();
			}
		}

		private new void Select()
		{
			Hover();
			EnableTilt();
			EnableOuterGlow(state: true);
			EnableIdleAnimation(state: false);
			SetSelectedDescription();
		}

		private void Deselect()
		{
			UnHover();
			DisableTilt();
			EnableOuterGlow(state: false);
			EnableIdleAnimation(state: true);
			SetDeselectedDescription();
		}

		public void SetSelectedDescription()
		{
			descriptionCanvasGroup.alpha = selectedDescriptionAlpha;
		}

		public void SetDeselectedDescription()
		{
			descriptionCanvasGroup.alpha = deselectedDescriptionAlpha;
		}

		public override Selectable FindSelectableOnDown()
		{
			return null;
		}

		public override Selectable FindSelectableOnUp()
		{
			return null;
		}

		public override Selectable FindSelectableOnLeft()
		{
			for (int i = 0; i < Selectable.s_SelectableCount; i++)
			{
				Selectable selectable = base.FindSelectableOnLeft();
				if (selectable != null && selectable.TryGetComponent<PerkView>(out var component))
				{
					return component;
				}
			}
			return null;
		}

		public override Selectable FindSelectableOnRight()
		{
			for (int i = 0; i < Selectable.s_SelectableCount; i++)
			{
				Selectable selectable = base.FindSelectableOnRight();
				if (selectable != null && selectable.TryGetComponent<PerkView>(out var component))
				{
					return component;
				}
			}
			return null;
		}

		public async UniTask ShowTween(Transform startPosition)
		{
			await UniTask.NextFrame();
			glowEffect.SetActive(value: false);
			descriptionCanvasGroup.alpha = 0f;
			Vector3 position = shakeParent.position;
			shakeParent.position = startPosition.position;
			Tween t = Perk3DView.RotateOnPlaceEffect(animationSettings.SpawnRotationTime, animationSettings.SpawnRotationAmount);
			Tween t2 = shakeParent.DOMove(position, animationSettings.SpawnMoveTime);
			Tween t3 = descriptionCanvasGroup.DOFade(deselectedDescriptionAlpha, animationSettings.SpawnDescriptionFadeTime);
			t.SetEase(animationSettings.SpawnRotationEase.GetEaseFunction());
			t2.SetEase(animationSettings.SpawnMoveEase.GetEaseFunction());
			t3.SetEase(animationSettings.SpawnDescriptionFadeEase.GetEaseFunction());
			onShowSequence?.Kill();
			onShowSequence = DOTween.Sequence(this);
			onShowSequence.Insert(animationSettings.SpawnRotationDelay, t);
			onShowSequence.Insert(animationSettings.SpawnMoveDelay, t2);
			onShowSequence.Insert(animationSettings.SpawnDescriptionFadeDelay, t3);
			onShowSequence.SetUpdate(DG.Tweening.UpdateType.Normal, isIndependentUpdate: true);
			onShowSequence.Play();
			await onShowSequence.AsyncWaitForCompletion();
			glowEffect.SetActive(value: true);
		}

		public async UniTask HideTween(Transform endPosition)
		{
			await UniTask.NextFrame();
			DisableTilt(instant: true);
			EnableIdleAnimation(state: false);
			glowEffect.SetActive(value: false);
			Tween t = Perk3DView.RotateOnPlaceEffect(animationSettings.DespawnRotationTime, animationSettings.DespawnRotationAmount);
			Tween t2 = shakeParent.DOMove(endPosition.position, animationSettings.DespawnMoveTime);
			Tween t3 = descriptionCanvasGroup.DOFade(0f, animationSettings.DespawnDescriptionFadeTime);
			t.SetEase(animationSettings.DespawnRotationEase.GetEaseFunction());
			t2.SetEase(animationSettings.DespawnMoveEase.GetEaseFunction());
			t3.SetEase(animationSettings.DespawnDescriptionFadeEase.GetEaseFunction());
			onHideSequence?.Kill();
			onHideSequence = DOTween.Sequence(this);
			onHideSequence.Insert(animationSettings.DespawnMoveDelay, t);
			onHideSequence.Insert(animationSettings.DespawnRotationDelay, t2);
			onHideSequence.Insert(animationSettings.DespawnDescriptionFadeDelay, t3);
			onHideSequence.SetUpdate(DG.Tweening.UpdateType.Normal, isIndependentUpdate: true);
			onHideSequence.Play();
			await onHideSequence.AsyncWaitForCompletion();
		}

		private async UniTask PerkSelectedTween()
		{
			onPerkSelectedTweenStart?.Invoke();
			DisableTilt(instant: true);
			EnableIdleAnimation(state: false);
			perkSelectedSequence?.Kill();
			perkSelectedSequence = DOTween.Sequence(this);
			Tween t = Perk3DView.RotateOnPlaceEffect(animationSettings.SelectedRotationTime, animationSettings.SelectedRotationAmount);
			Tween t2 = Perk3DView.VerticalPunch(animationSettings.SelectedVerticalStrength, animationSettings.SelectedVerticalTime, animationSettings.SelectedVerticalVibrato, animationSettings.SelectedVerticalElasticity);
			t.SetEase(animationSettings.SelectedRotationEase.GetEaseFunction());
			t2.SetEase(animationSettings.SelectedVerticalEase.GetEaseFunction());
			perkSelectedSequence.Append(t2);
			perkSelectedSequence.Join(t);
			perkSelectedSequence.SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
			PlayEquipParticleSystem();
			await perkSelectedSequence.AsyncWaitForCompletion();
			OnSubmit?.Invoke(PerkPoolID.Beatrice, _perkData);
		}

		private async UniTask HideAnimation()
		{
			await UniTask.NextFrame();
			_openCloseAnimationState = animancer.Layers[0].Play(closeAnimation, closeAnimation.FadeDuration);
			while (_openCloseAnimationState.IsPlayingAndNotEnding())
			{
				await UniTask.NextFrame();
			}
		}

		public void PlayEquipParticleSystem()
		{
			equipParticleSystem.Clear();
			equipParticleSystem.Play();
		}
	}
}
