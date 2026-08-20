using System.Collections;
using System.Linq;
using System.Threading;
using AstralShift.HellMaiden.Data;
using AstralShift.Helpers;
using Coffee.UIExtensions;
using Com.LuisPedroFonseca.ProCamera2D;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI.Cards
{
	public class UICardView : ViewFollower
	{
		[Header("References")]
		[SerializeField]
		protected UICardViewHandler viewHandler;

		[SerializeField]
		protected Canvas canvas;

		[SerializeField]
		protected int topMostSortingOrder;

		[SerializeField]
		protected UICard3DProxy card3DProxy;

		[Space]
		[SerializeField]
		private Transform shakeParent;

		[Header("Outer Glow VFX")]
		[SerializeField]
		private RawImage selectionGlow;

		[SerializeField]
		private RawImage weaponCompatGlow;

		[SerializeField]
		private RawImage mergeCompatGlow;

		[SerializeField]
		private RawImage reRollGlow;

		[Header("Rarity VFX")]
		[SerializeField]
		private CanvasGroup silverRarityGlow;

		[SerializeField]
		private Image silverRarityGlowImage;

		[SerializeField]
		private CanvasGroup goldRarityGlow;

		[SerializeField]
		private Image goldRarityGlowImage;

		[SerializeField]
		private CanvasGroup prismRarityGlow;

		[SerializeField]
		private Image prismRarityGlowImage;

		[Header("Place VFX")]
		[SerializeField]
		private UIParticle equipParticleSystem;

		private Sequence _equipEffect;

		[Header("Merge VFX")]
		[SerializeField]
		private UIParticle mergeParticleSystem;

		[Header("Discard VFX")]
		[SerializeField]
		protected UIParticle discardFadeParticleSystem;

		[SerializeField]
		protected float discardParticleSystemMinHeight = -150f;

		[SerializeField]
		protected float discardParticleSystemMaxHeight = 150f;

		[SerializeField]
		protected UIParticle discardExplosionParticleSystem;

		[Header("Banish VFX")]
		[SerializeField]
		protected UIParticle banishFadeParticleSystem;

		[SerializeField]
		protected float banishParticleSystemMinHeight = -150f;

		[SerializeField]
		protected float banishParticleSystemMaxHeight = 150f;

		[SerializeField]
		protected UIParticle banishExplosionParticleSystem;

		private Transform _transform;

		private bool _lockAllMotion;

		private bool _rotationFollow = true;

		private bool _canTilt;

		private bool _allowMovement = true;

		private bool _allowIdleFloat = true;

		private float _idleOffset;

		private int _idleOffsetSign = 1;

		private Vector3 _rotationSmoothDelta;

		private Vector3 _movementSmoothDelta;

		private float _containerRotationDelta;

		private float _followTargetRotationInfluence;

		private Vector3 _previousScale;

		private Vector3 _nextScale;

		private float _scaleSensibilityTimeout;

		private Transform _beforeMagnetParent;

		private Vector3 _beforeMagnetScale = Vector3.one;

		private Coroutine _magnetLockPositionCoroutine;

		private Tween _idleOffsetTween;

		private Tween _hoverRotateTween;

		private Tween _hoverScaleTween;

		private Tween _magnetScaleTween;

		private Tween _magnetLockRotationTween;

		private Sequence _selectionOuterGlowTween;

		private Sequence _contextualInnerGlowVFXTween;

		private Sequence _sheenSequence;

		private Sequence _mergeGlowSequence;

		private Sequence _reRollSequence;

		private Sequence _discardSequence;

		private Sequence _banishSequence;

		private Sequence _weaponCompatibilityTween;

		private Sequence _weaponUnCompatibilityTween;

		private Sequence _mergeCompatibilityTween;

		private Sequence _rarityFadeTween;

		private Sequence _rarityScaleBlinkTween;

		private Camera _camera;

		private const float ContextualInnerGlowVFXStopTime = 0.1f;

		private const float ContextualInnerGlowIntensity = 6f;

		private const float DiscardFadeAmountRange = 1.1f;

		private const float DiscardFadeAmountMin = -0.1f;

		private const float DiscardFadeAmountMax = 1f;

		private const float BanishFadeAmountRange = 1.1f;

		private const float BanishFadeAmountMin = -0.1f;

		private const float BanishFadeAmountMax = 1f;

		public Canvas Canvas => canvas;

		public UICard3DProxy Card3DProxy => card3DProxy;

		public UIParticle DiscardFadeParticleSystem => discardFadeParticleSystem;

		public float DiscardParticleSystemMinHeight => discardParticleSystemMinHeight;

		public float DiscardParticleSystemMaxHeight => discardParticleSystemMaxHeight;

		public UIParticle DiscardExplosionParticleSystem => discardExplosionParticleSystem;

		public UIParticle BanishFadeParticleSystem => banishFadeParticleSystem;

		public float BanishParticleSystemMinHeight => banishParticleSystemMinHeight;

		public float BanishParticleSystemMaxHeight => banishParticleSystemMaxHeight;

		public UIParticle BanishExplosionParticleSystem => banishExplosionParticleSystem;

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

		protected virtual void Awake()
		{
			if ((bool)selectionGlow)
			{
				selectionGlow.color = new Color(1f, 1f, 1f, 0f);
			}
			if ((bool)weaponCompatGlow)
			{
				weaponCompatGlow.color = new Color(1f, 1f, 1f, 0f);
			}
			if ((bool)mergeCompatGlow)
			{
				mergeCompatGlow.color = new Color(1f, 1f, 1f, 0f);
			}
			if ((bool)reRollGlow)
			{
				reRollGlow.color = new Color(1f, 1f, 1f, 0f);
			}
			EnableRarityVFX(state: false, instant: true);
		}

		public void Init(UICardViewHandler viewHandler)
		{
			this.viewHandler = viewHandler;
			card3DProxy.Initialize(viewHandler);
			_camera = ProCamera2D.Instance.GameCamera;
		}

		public void SetRenderOrderTopMost()
		{
			Canvas.overrideSorting = true;
			Canvas.sortingOrder = topMostSortingOrder;
		}

		public void SetRenderOrderToDefault()
		{
			Canvas.overrideSorting = false;
		}

		public void ForceStaticRender(bool state)
		{
			Card3DProxy.Card.ForceStatic = state;
		}

		public void EnableStaticRender(bool state)
		{
			Card3DProxy.Card.CanBeStatic = state;
		}

		public void EnqueueRender()
		{
			Card3DProxy.Card.EnqueueRender();
		}

		public void Dispose()
		{
			Hide();
			UICardRenderingManager.Instance.RemoveCard(viewHandler);
			Object.Destroy(Card3DProxy.gameObject);
			Object.Destroy(base.gameObject);
		}

		private void OnDestroy()
		{
			HideAllOuterGlows();
			DOTween.Kill(this);
			Hide();
			if ((bool)viewHandler)
			{
				UICardRenderingManager.Instance.RemoveCard(viewHandler);
			}
			Object.Destroy(selectionGlow.material);
			if ((bool)Card3DProxy)
			{
				Object.Destroy(Card3DProxy.gameObject);
			}
		}

		protected override void LateUpdate()
		{
			if (!viewHandler || !_camera)
			{
				return;
			}
			if (_lockAllMotion)
			{
				ApplyViewPortPositionTo3DView();
				return;
			}
			if (viewHandler.IsDropped)
			{
				HardScaleFollow();
			}
			else
			{
				ApplyViewPortPositionTo3DView();
				SmoothScaleFollow();
			}
			if (_allowMovement)
			{
				if (viewHandler.IsDropped)
				{
					HardRotationFollow();
				}
				if (_rotationFollow)
				{
					SmoothRotationFollow();
				}
				if (movementFollow)
				{
					SmoothMovementFollow();
				}
			}
		}

		public void LockAllMotion()
		{
			_lockAllMotion = true;
		}

		public void UnlockAllMotion()
		{
			_lockAllMotion = false;
		}

		public void EnableMovement()
		{
			_allowMovement = true;
		}

		public void DisableMovement()
		{
			_allowMovement = false;
		}

		protected override void SmoothMovementFollow()
		{
			float num = (_allowIdleFloat ? _idleOffset : 0f);
			Transform.position = Vector3.Lerp(base.transform.position, followTransform.position + Vector3.up * num, animationSettings.FollowSpeed * Time.unscaledDeltaTime);
		}

		public void SnapTransformToTarget()
		{
			if ((bool)followTransform)
			{
				Transform.SetPositionAndRotation(followTransform.position, followTransform.rotation);
				Transform.localScale = viewHandler.transform.lossyScale / (viewHandler.Canvas ? viewHandler.Canvas.scaleFactor : 1f);
			}
		}

		private void InitIdleAnimation()
		{
			float resAdjustedScreenSpaceOffset = UIResolutionHelpers.GetResAdjustedScreenSpaceOffset(animationSettings.IdleMoveOffset);
			RefreshIdleAnimationOffset();
			_idleOffsetTween = DOTween.To(() => _idleOffset, delegate(float value)
			{
				_idleOffset = (float)_idleOffsetSign * value;
			}, resAdjustedScreenSpaceOffset, animationSettings.IdleMoveTime).SetEase(animationSettings.IdleMoveOffsetEase.GetEaseFunction()).SetLoops(-1, LoopType.Yoyo)
				.SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
		}

		public void RefreshIdleAnimationOffset()
		{
			_idleOffsetSign = ((viewHandler.SiblingIndex % 2 != 0) ? 1 : (-1));
		}

		public void EnableIdleAnimation(bool state)
		{
			_allowIdleFloat = state;
			if (!_idleOffsetTween.IsActive())
			{
				InitIdleAnimation();
			}
		}

		public void ApplyTilt(Vector2 input, bool isPosition = true)
		{
			if (_canTilt && (bool)Card3DProxy)
			{
				float hoverTiltAmount = animationSettings.HoverTiltAmount;
				float hoverTiltSpeed = animationSettings.HoverTiltSpeed;
				Vector2 direction = input;
				if (isPosition)
				{
					Vector2 vector = _camera.ScreenToViewportPoint(Transform.position);
					direction = (Vector2)_camera.ScreenToViewportPoint(input) - vector;
				}
				Card3DProxy?.Card.ApplyTilt(direction, hoverTiltAmount, hoverTiltSpeed);
			}
		}

		public void ApplyTilt(Vector2 direction, float magnitude)
		{
			if (_canTilt && !(Card3DProxy == null))
			{
				float hoverTiltSpeed = animationSettings.HoverTiltSpeed;
				Card3DProxy?.Card.ApplyTilt(direction.normalized, magnitude, hoverTiltSpeed);
			}
		}

		public void EnableTilt()
		{
			_canTilt = true;
		}

		public void StopTilt()
		{
			Card3DProxy?.Card.StopTilt(animationSettings.HoverTiltStopSpeed);
		}

		public void DisableTilt(bool instant = false)
		{
			_canTilt = false;
			if (instant)
			{
				Card3DProxy?.Card?.StopTiltInstant();
			}
			else
			{
				Card3DProxy?.Card?.StopTilt(animationSettings.HoverTiltStopSpeed);
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
			_hoverRotateTween = shakeParent.DOPunchRotation(Vector3.forward * hoverPunchAngle, hoverRotationTime, hoverVibration, hoverElasticity).SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true).SetTarget(this);
			_hoverScaleTween = shakeParent.DOScale(hoverScaleMultiplier, hoverScaleTime).SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true).SetTarget(this);
			_hoverRotateTween.Play();
			_hoverScaleTween.Play();
		}

		public void UnHover()
		{
			float hoverScaleTime = animationSettings.HoverScaleTime;
			_hoverRotateTween?.Kill(complete: true);
			_hoverScaleTween?.Kill();
			_hoverScaleTween = shakeParent.DOScale(Vector3.one, hoverScaleTime).SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true).SetTarget(this);
			_hoverScaleTween.Play();
		}

		public void SetSelectionVFX(Material material)
		{
			HideAllOuterGlows();
			if ((bool)selectionGlow)
			{
				selectionGlow.material = new Material(material);
				selectionGlow.color = new Color(1f, 1f, 1f, 0f);
			}
		}

		public void EnableSelectionOuterGlow(bool state)
		{
			if ((bool)selectionGlow)
			{
				if (!selectionGlow.texture)
				{
					selectionGlow.texture = UICardRenderingManager.Instance.GetCardDynamicTexture(viewHandler);
				}
				if (state)
				{
					HideAllOuterGlows();
				}
				float hoverGlowFadeTime = animationSettings.HoverGlowFadeTime;
				_selectionOuterGlowTween?.Kill();
				_selectionOuterGlowTween = DOTween.Sequence(this);
				_selectionOuterGlowTween.Append(selectionGlow.DOFade(state ? 1 : 0, hoverGlowFadeTime));
				_selectionOuterGlowTween.SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
				_selectionOuterGlowTween.SetLink(selectionGlow.gameObject);
			}
		}

		public void EnableRotationFollow()
		{
			_rotationFollow = true;
		}

		public void DisableRotationFollow()
		{
			_rotationFollow = false;
		}

		private void SmoothRotationFollow()
		{
			Vector3 position = Transform.position;
			Vector3 position2 = followTransform.position;
			if (viewHandler.IsDragging || !((position - position2).sqrMagnitude < 0.0001f))
			{
				Vector3 vector = _camera.ScreenToViewportPoint(Transform.position);
				Vector3 vector2 = _camera.ScreenToViewportPoint(followTransform.position);
				Vector3 vector3 = vector - vector2;
				_movementSmoothDelta = Vector3.Lerp(_movementSmoothDelta, vector3, animationSettings.FollowRotationSpeed * Time.unscaledDeltaTime);
				Vector3 vector4 = _movementSmoothDelta * animationSettings.FollowRotationAmount;
				if (viewHandler.IsDragging)
				{
					_followTargetRotationInfluence -= Time.unscaledDeltaTime * animationSettings.FollowRotationSpeed;
				}
				else
				{
					vector4 = Vector3.Lerp(vector4, vector3 * animationSettings.FollowRotationAmount, Time.unscaledDeltaTime * animationSettings.FollowRotationSpeed);
					_followTargetRotationInfluence += Time.unscaledDeltaTime * animationSettings.FollowRotationSpeed;
				}
				_followTargetRotationInfluence = Mathf.Clamp01(_followTargetRotationInfluence);
				_rotationSmoothDelta = Vector3.Lerp(_rotationSmoothDelta, vector4, animationSettings.FollowRotationSpeed * Time.unscaledDeltaTime);
				float z = followTransform.eulerAngles.z;
				z = ((z > 180f) ? (z - 360f) : z);
				float z2 = Mathf.Lerp(Mathf.Clamp(_rotationSmoothDelta.x, 0f - animationSettings.FollowRotationMaxAngle, animationSettings.FollowRotationMaxAngle), z, _followTargetRotationInfluence);
				Vector3 eulerAngles = Transform.eulerAngles;
				Quaternion rotation = Quaternion.Euler(eulerAngles.x, eulerAngles.y, z2);
				Transform.rotation = rotation;
			}
		}

		private void HardRotationFollow()
		{
			Transform.eulerAngles = followTransform.eulerAngles;
		}

		public void ApplyRotationOffset(Vector3 offset)
		{
			if ((bool)Card3DProxy && (bool)Card3DProxy.Card)
			{
				Card3DProxy.Card.ApplyRotationOffset(offset);
			}
		}

		private void ApplyViewPortPositionTo3DView()
		{
			if ((bool)Card3DProxy && (bool)Card3DProxy.Card)
			{
				Vector2 viewPortPosition = _camera.ScreenToViewportPoint(Transform.position);
				Card3DProxy.Card.SetViewPortPosition(viewPortPosition);
			}
		}

		private void HardScaleFollow()
		{
			Transform.localScale = viewHandler.GlobalScale;
		}

		private void SmoothScaleFollow()
		{
			if (_nextScale == Vector3.zero)
			{
				_previousScale = viewHandler.GlobalScale;
				_nextScale = viewHandler.GlobalScale;
			}
			if (!Mathf.Approximately(_previousScale.sqrMagnitude, viewHandler.GlobalScale.sqrMagnitude))
			{
				_scaleSensibilityTimeout += Time.unscaledDeltaTime;
				if (_scaleSensibilityTimeout >= animationSettings.FollowScaleSensibilityTime)
				{
					_nextScale = viewHandler.GlobalScale;
					_previousScale = _nextScale;
					_scaleSensibilityTimeout = 0f;
				}
			}
			else
			{
				_scaleSensibilityTimeout = 0f;
			}
			Transform.localScale = Vector3.Lerp(Transform.localScale, _nextScale, Time.unscaledDeltaTime * animationSettings.FollowScaleSpeed);
		}

		public UniTask MagnetLock(Transform magnetTransform, Vector3 rotation)
		{
			if (_lockAllMotion || !viewHandler.IsDragging || magnetTransform == null)
			{
				return UniTask.CompletedTask;
			}
			ShowMagnetLockGlow(state: true);
			movementFollow = false;
			_rotationFollow = false;
			_beforeMagnetParent = viewHandler.Transform.parent;
			_beforeMagnetScale = viewHandler.Transform.localScale;
			viewHandler.Transform.SetParent(magnetTransform);
			viewHandler.Transform.localScale = Vector3.one;
			_magnetLockRotationTween?.Kill();
			DisableTilt();
			if (_magnetLockPositionCoroutine != null)
			{
				StopCoroutine(_magnetLockPositionCoroutine);
			}
			_magnetLockPositionCoroutine = StartCoroutine(MagnetLockPosition(magnetTransform));
			_magnetLockRotationTween = animationSettings.MagnetLockRotationEase.AddEase(base.transform.DOLocalRotate(rotation, animationSettings.MagnetLockRotationSpeed)).SetSpeedBased(isSpeedBased: true).SetUpdate(isIndependentUpdate: true);
			_magnetLockRotationTween.SetTarget(this);
			_magnetLockRotationTween.Play();
			return UniTask.Delay((int)(1000f / animationSettings.MagnetLockMovementSpeed), ignoreTimeScale: true, PlayerLoopTiming.LastUpdate);
		}

		private IEnumerator MagnetLockPosition(Transform target)
		{
			Vector3 start = Transform.position;
			float totalTime = 1f / animationSettings.MagnetLockMovementSpeed;
			for (float timePassed = 0f; timePassed < totalTime; timePassed += Time.unscaledDeltaTime)
			{
				float t = timePassed / totalTime;
				t = animationSettings.MagnetLockMovementEase.EasePercentage(t);
				Transform.position = Vector3.Lerp(start, target.position, t);
				yield return null;
			}
			while (true)
			{
				Transform.position = target.position;
				yield return null;
			}
		}

		public void KillMagnetLock(Transform returnParent = null)
		{
			if (!_lockAllMotion)
			{
				if (_magnetLockPositionCoroutine != null)
				{
					StopCoroutine(_magnetLockPositionCoroutine);
					_magnetLockPositionCoroutine = null;
				}
				ShowMagnetLockGlow(state: false);
				_magnetLockRotationTween?.Kill();
				if ((bool)returnParent)
				{
					viewHandler.SetParentContainer(returnParent);
				}
				else if (!_beforeMagnetParent)
				{
					viewHandler.SetParentToOnDragContainer();
				}
				else
				{
					viewHandler.SetParentContainer(_beforeMagnetParent);
				}
				viewHandler.Transform.localScale = _beforeMagnetScale;
				_beforeMagnetParent = null;
				movementFollow = true;
				_rotationFollow = true;
			}
		}

		private void HideAllOuterGlows()
		{
			EnableSelectionOuterGlow(state: false);
			EnableMergeOuterGlow(state: false);
			EnableWeaponCompatGlow(state: false);
		}

		private void StartContextualInnerGlowVFX(Color32 color, float minFadeAmount, float maxFadeAmount, float fadeDuration, float pulseDuration)
		{
			Material material = Card3DProxy?.Get2DMaterial();
			if ((bool)material)
			{
				material.SetFloat(Allin1ShaderProps.OverlayGlow, 6f);
				_contextualInnerGlowVFXTween?.Kill();
				_contextualInnerGlowVFXTween = DOTween.Sequence(this);
				_contextualInnerGlowVFXTween.Append(material.DOFloat(maxFadeAmount, Allin1ShaderProps.OverlayBlend, fadeDuration));
				_contextualInnerGlowVFXTween.Join(material.DOColor(color, Allin1ShaderProps.OverlayColor, fadeDuration));
				_contextualInnerGlowVFXTween.OnComplete(delegate
				{
					StartContextualInnerGlowPulse(minFadeAmount, pulseDuration);
				});
				_contextualInnerGlowVFXTween.SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
			}
		}

		private void StartContextualInnerGlowPulse(float minFadeAmount, float pulseDuration)
		{
			Material material = Card3DProxy?.Get2DMaterial();
			if ((bool)material)
			{
				_contextualInnerGlowVFXTween?.Kill();
				_contextualInnerGlowVFXTween = DOTween.Sequence(this);
				_contextualInnerGlowVFXTween.Append(material.DOFloat(minFadeAmount, Allin1ShaderProps.OverlayBlend, pulseDuration).SetEase(Ease.InOutQuad));
				_contextualInnerGlowVFXTween.OnComplete(delegate
				{
					material.SetColor(Allin1ShaderProps.OverlayColor, Color.white);
				});
				_contextualInnerGlowVFXTween.SetLoops(-1, LoopType.Yoyo);
				_contextualInnerGlowVFXTween.SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
			}
		}

		private void StopContextualInnerGlowVFX(bool instant = false)
		{
			Material material = Card3DProxy?.Get2DMaterial();
			if ((bool)material)
			{
				_contextualInnerGlowVFXTween?.Kill();
				if (instant)
				{
					material.SetFloat(Allin1ShaderProps.OverlayBlend, 0f);
					return;
				}
				_contextualInnerGlowVFXTween = DOTween.Sequence(this);
				_contextualInnerGlowVFXTween.Append(material.DOFloat(0f, Allin1ShaderProps.OverlayBlend, 0.1f).SetEase(Ease.InOutQuad));
				_contextualInnerGlowVFXTween.SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
			}
		}

		public void ShowMagnetLockGlow(bool state)
		{
			if (state)
			{
				Color color = animationSettings.MagnetLockOverlayColor;
				float magnetLockOverlayMinFadeAmount = animationSettings.MagnetLockOverlayMinFadeAmount;
				float magnetLockOverlayMaxFadeAmount = animationSettings.MagnetLockOverlayMaxFadeAmount;
				float magnetLockOverlayFadeTime = animationSettings.MagnetLockOverlayFadeTime;
				float magnetLockOverlayBlinkTime = animationSettings.MagnetLockOverlayBlinkTime;
				HideAllOuterGlows();
				EnableSelectionOuterGlow(state: true);
				StartContextualInnerGlowVFX(color, magnetLockOverlayMinFadeAmount, magnetLockOverlayMaxFadeAmount, magnetLockOverlayFadeTime, magnetLockOverlayBlinkTime);
			}
			else
			{
				EnableSelectionOuterGlow(state: false);
				StopContextualInnerGlowVFX();
			}
		}

		public async UniTask EquipEffect()
		{
			await UniTask.NextFrame();
			DisableTilt(instant: true);
			_equipEffect?.Kill();
			_equipEffect = DOTween.Sequence(this);
			Card3DProxy.Card.Transform.rotation = Quaternion.identity;
			_equipEffect.Append(Card3DProxy.Card.RotateOnPlaceEffect(animationSettings.EquipRotationTime, 360f).SetEase(animationSettings.EquipRotationEase.GetEaseFunction()));
			_equipEffect.SetUpdate(isIndependentUpdate: true);
			_equipEffect.OnComplete(PlayEquipParticleSystem);
			await _equipEffect.ToUniTask(TweenCancelBehaviour.KillAndCancelAwait, base.destroyCancellationToken);
		}

		public async UniTask EquipEffectOnMerge()
		{
			_equipEffect?.Kill();
			_equipEffect = DOTween.Sequence(this);
			Card3DProxy.Card.Transform.rotation = Quaternion.identity;
			_equipEffect.Append(Card3DProxy.Card.Tilt(Vector3.up, 0f, animationSettings.EquipScaleTime));
			_equipEffect.Join(Sheen(animationSettings.MergeEndSheenTime).SetEase(animationSettings.MergeEndSheenEase.GetEaseFunction()));
			_equipEffect.SetUpdate(isIndependentUpdate: true);
			_equipEffect.OnComplete(PlayEquipParticleSystem);
			await _equipEffect.ToUniTask(TweenCancelBehaviour.KillAndCancelAwait, base.destroyCancellationToken);
		}

		public Tween Sheen(float duration)
		{
			Material material = Card3DProxy?.Get2DMaterial();
			if (!material)
			{
				return null;
			}
			_sheenSequence?.Kill();
			_sheenSequence = DOTween.Sequence(this);
			material.SetFloat(Allin1ShaderProps.ShineLocation, 1f);
			material.SetFloat(Allin1ShaderProps.ShineWidth, animationSettings.MergeEndSheenMinWidth);
			_sheenSequence.Append(material.DOFloat(0f, Allin1ShaderProps.ShineLocation, duration));
			_sheenSequence.Join(material.DOFloat(animationSettings.MergeEndSheenMaxWidth, Allin1ShaderProps.ShineWidth, duration / 2f));
			_sheenSequence.Insert(duration / 2f, material.DOFloat(animationSettings.MergeEndSheenMinWidth, Allin1ShaderProps.ShineWidth, duration / 2f));
			_sheenSequence.SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
			return _sheenSequence;
		}

		public void PlayEquipParticleSystem()
		{
			equipParticleSystem.Clear();
			equipParticleSystem.Play();
			equipParticleSystem.StartEmission();
		}

		public void ShowMergeMagnetLockVFX(bool state)
		{
			if (state)
			{
				Color color = animationSettings.MergeCompatibilityColor;
				float mergeCompatibilityMinFadeAmount = animationSettings.MergeCompatibilityMinFadeAmount;
				float mergeCompatibilityMaxFadeAmount = animationSettings.MergeCompatibilityMaxFadeAmount;
				float mergeCompatibilityFadeTime = animationSettings.MergeCompatibilityFadeTime;
				float mergeCompatibilityMagnetLockBlinkTime = animationSettings.MergeCompatibilityMagnetLockBlinkTime;
				HideAllOuterGlows();
				EnableMergeOuterGlow(state: true);
				StartContextualInnerGlowVFX(color, mergeCompatibilityMinFadeAmount, mergeCompatibilityMaxFadeAmount, mergeCompatibilityFadeTime, mergeCompatibilityMagnetLockBlinkTime);
			}
			else
			{
				EnableMergeOuterGlow(state: false);
				StopContextualInnerGlowVFX();
			}
		}

		public void EnableMergeOuterGlow(bool state)
		{
			if ((bool)mergeCompatGlow)
			{
				if (!mergeCompatGlow.texture)
				{
					mergeCompatGlow.texture = UICardRenderingManager.Instance.GetCardDynamicTexture(viewHandler);
				}
				float mergeCompatibilityFadeTime = animationSettings.MergeCompatibilityFadeTime;
				_mergeCompatibilityTween?.Kill();
				_mergeCompatibilityTween = DOTween.Sequence(this);
				_mergeCompatibilityTween.Append(mergeCompatGlow.DOFade(state ? 1 : 0, mergeCompatibilityFadeTime));
				_mergeCompatibilityTween.SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
			}
		}

		public Tween MergeGlowIn(float duration)
		{
			Material material = Card3DProxy?.Get2DMaterial();
			if (!material)
			{
				return null;
			}
			float mergeGlowColorIntensity = animationSettings.MergeGlowColorIntensity;
			float mergeGlowGlobalIntensity = animationSettings.MergeGlowGlobalIntensity;
			float mergeGlowBlurIntensity = animationSettings.MergeGlowBlurIntensity;
			float mergeGlowChromaticAberrationIntensity = animationSettings.MergeGlowChromaticAberrationIntensity;
			_mergeGlowSequence?.Kill();
			_mergeGlowSequence = DOTween.Sequence(this);
			material.SetColor(Allin1ShaderProps.GlowColor, Color.white);
			_mergeGlowSequence.Append(material.DOFloat(mergeGlowColorIntensity, Allin1ShaderProps.GlowColorIntensity, duration));
			_mergeGlowSequence.Join(material.DOFloat(mergeGlowGlobalIntensity, Allin1ShaderProps.GlobalGlowIntensity, duration));
			_mergeGlowSequence.Join(material.DOFloat(mergeGlowBlurIntensity, Allin1ShaderProps.BlurIntensity, duration));
			_mergeGlowSequence.Join(material.DOFloat(mergeGlowChromaticAberrationIntensity, Allin1ShaderProps.ChromaticAberrationAmount, duration));
			_mergeGlowSequence.Join(material.DOFloat(1f, Allin1ShaderProps.Brightness, duration));
			_mergeGlowSequence.SetEase(animationSettings.MergeStartGlowEase.GetEaseFunction());
			return _mergeGlowSequence;
		}

		public Tween MergeGlowOut(float duration)
		{
			Material material = Card3DProxy?.Get2DMaterial();
			if (!material)
			{
				return null;
			}
			_mergeGlowSequence?.Kill();
			_mergeGlowSequence = DOTween.Sequence(this);
			material.SetColor(Allin1ShaderProps.GlowColor, Color.white);
			_mergeGlowSequence.Append(material.DOFloat(0f, Allin1ShaderProps.GlowColorIntensity, duration));
			_mergeGlowSequence.Join(material.DOFloat(1f, Allin1ShaderProps.GlobalGlowIntensity, duration));
			_mergeGlowSequence.Join(material.DOFloat(0f, Allin1ShaderProps.BlurIntensity, duration));
			_mergeGlowSequence.Join(material.DOFloat(0f, Allin1ShaderProps.ChromaticAberrationAmount, duration));
			_mergeGlowSequence.Join(material.DOFloat(0f, Allin1ShaderProps.Brightness, duration));
			_mergeGlowSequence.SetEase(animationSettings.MergeEndGlowEase.GetEaseFunction());
			return _mergeGlowSequence;
		}

		public void PlayMergeParticleSystem()
		{
			mergeParticleSystem.Clear();
			mergeParticleSystem.Play();
			mergeParticleSystem.StartEmission();
		}

		public Tween MotionBlur(float startDistance, float endDistance, float localAngleDegrees, float duration)
		{
			Material material = Card3DProxy?.Get2DMaterial();
			if (!material)
			{
				return null;
			}
			float value = math.remap(-360f, 360f, -1f, 1f, localAngleDegrees);
			material.SetFloat(Allin1ShaderProps.MotionBlurDistance, startDistance);
			material.SetFloat(Allin1ShaderProps.MotionBlurAngle, value);
			return material.DOFloat(endDistance, Allin1ShaderProps.MotionBlurDistance, duration).SetTarget(this);
		}

		public void EnableMotionBlur(bool state)
		{
			Material material = Card3DProxy?.Get2DMaterial();
			if ((bool)material)
			{
				if (state)
				{
					material.EnableKeyword(Allin1ShaderProps.MotionBlurOn);
				}
				else
				{
					material.DisableKeyword(Allin1ShaderProps.MotionBlurOn);
				}
			}
		}

		public async UniTask PlayReRollAnimation(float duration, CancellationToken cancellationToken = default(CancellationToken))
		{
			Material material = Card3DProxy?.Get2DMaterial();
			if (!material || !reRollGlow)
			{
				return;
			}
			if (!reRollGlow.texture)
			{
				reRollGlow.texture = UICardRenderingManager.Instance.GetCardDynamicTexture(viewHandler);
			}
			HideAllOuterGlows();
			_reRollSequence?.Kill();
			_reRollSequence = DOTween.Sequence(this);
			float reRollPunchAngle = animationSettings.ReRollPunchAngle;
			int reRollPunchVibrato = animationSettings.ReRollPunchVibrato;
			float reRollPunchRandomness = animationSettings.ReRollPunchRandomness;
			EaseFunction easeFunction = animationSettings.ReRollPunchEase.GetEaseFunction();
			Vector3 endValue = viewHandler.GlobalScale * animationSettings.ReRollScaleMultiplier;
			Color endValue2 = animationSettings.ReRollGlowColor;
			float reRollGlowFadeAmount = animationSettings.ReRollGlowFadeAmount;
			float reRollGlowOverlayAmount = animationSettings.ReRollGlowOverlayAmount;
			_reRollSequence.Append(Transform.DOShakeRotation(duration, Vector3.forward * reRollPunchAngle, reRollPunchVibrato, reRollPunchRandomness, fadeOut: false, ShakeRandomnessMode.Harmonic).SetEase(easeFunction));
			_reRollSequence.Join(Transform.DOScale(endValue, duration));
			_reRollSequence.Join(material.DOFloat(reRollGlowFadeAmount, Allin1ShaderProps.OverlayBlend, duration));
			_reRollSequence.Join(material.DOFloat(reRollGlowOverlayAmount, Allin1ShaderProps.OverlayGlow, duration));
			_reRollSequence.Join(material.DOColor(endValue2, Allin1ShaderProps.OverlayColor, duration));
			_reRollSequence.Join(reRollGlow.DOFade(1f, duration));
			_reRollSequence.SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
			try
			{
				await _reRollSequence.ToUniTask(TweenCancelBehaviour.KillAndCancelAwait, cancellationToken);
			}
			finally
			{
				_reRollSequence = null;
			}
		}

		public async UniTask PlayReRollDragAnimation(float duration, Transform targetTransform, CancellationToken cancellationToken = default(CancellationToken))
		{
			Material material = Card3DProxy?.Get2DMaterial();
			if (!material || !reRollGlow)
			{
				return;
			}
			if (!reRollGlow.texture)
			{
				reRollGlow.texture = UICardRenderingManager.Instance.GetCardDynamicTexture(viewHandler);
			}
			_reRollSequence?.Kill();
			_reRollSequence = DOTween.Sequence(this);
			float reRollPunchAngle = animationSettings.ReRollPunchAngle;
			int reRollPunchVibrato = animationSettings.ReRollPunchVibrato;
			float reRollPunchRandomness = animationSettings.ReRollPunchRandomness;
			EaseFunction easeFunction = animationSettings.ReRollPunchEase.GetEaseFunction();
			Vector3 endValue = viewHandler.GlobalScale * animationSettings.ReRollScaleMultiplier;
			Color endValue2 = animationSettings.ReRollGlowColor;
			float reRollGlowFadeAmount = animationSettings.ReRollGlowFadeAmount;
			float reRollGlowOverlayAmount = animationSettings.ReRollGlowOverlayAmount;
			_reRollSequence.Append(Transform.DOShakeRotation(duration, Vector3.forward * reRollPunchAngle, reRollPunchVibrato, reRollPunchRandomness, fadeOut: false, ShakeRandomnessMode.Harmonic).SetEase(easeFunction));
			_reRollSequence.Join(Transform.DOMove(targetTransform.position, duration / 3f));
			_reRollSequence.Join(Transform.DOScale(endValue, duration));
			_reRollSequence.Join(material.DOFloat(reRollGlowFadeAmount, Allin1ShaderProps.OverlayBlend, duration));
			_reRollSequence.Join(material.DOFloat(reRollGlowOverlayAmount, Allin1ShaderProps.OverlayGlow, duration));
			_reRollSequence.Join(material.DOColor(endValue2, Allin1ShaderProps.OverlayColor, duration));
			_reRollSequence.Join(reRollGlow.DOFade(1f, duration));
			_reRollSequence.SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
			try
			{
				await _reRollSequence.ToUniTask(TweenCancelBehaviour.KillAndCancelAwait, cancellationToken);
			}
			finally
			{
				_reRollSequence = null;
			}
		}

		public void StopReRollAnimation()
		{
			Material material = Card3DProxy?.Get2DMaterial();
			if ((bool)material)
			{
				_reRollSequence?.Kill();
				_reRollSequence = DOTween.Sequence(this);
				float reRollStopFadeTime = animationSettings.ReRollStopFadeTime;
				_reRollSequence.Append(material.DOFloat(0f, Allin1ShaderProps.OverlayBlend, reRollStopFadeTime));
				_reRollSequence.Join(reRollGlow.DOFade(0f, reRollStopFadeTime));
				_reRollSequence.SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
			}
		}

		public async UniTask PlayDiscardFadeAnimation(float duration, CancellationToken cancellationToken = default(CancellationToken))
		{
			Material material = Card3DProxy?.Get2DMaterial();
			if (!material)
			{
				return;
			}
			_discardSequence?.Kill();
			_discardSequence = DOTween.Sequence(this);
			SetRenderOrderTopMost();
			EnableSelectionOuterGlow(state: false);
			float discardPunchAngle = animationSettings.DiscardPunchAngle;
			int discardPunchVibrato = animationSettings.DiscardPunchVibrato;
			EaseFunction easeFunction = animationSettings.DiscardPunchEase.GetEaseFunction();
			Vector3 endValue = viewHandler.GlobalScale * animationSettings.DiscardScaleMultiplier;
			Color32 discardGlowColor = animationSettings.DiscardGlowColor;
			float discardOverlayBlendMaxAmount = animationSettings.DiscardOverlayBlendMaxAmount;
			float discardOverlayGlowAmount = animationSettings.DiscardOverlayGlowAmount;
			float num = material.GetFloat(Allin1ShaderProps.FadeAmount);
			float duration2 = duration * Mathf.Abs(1f - num) / 1.1f;
			Vector2 endValue2 = material.GetTextureOffset(Allin1ShaderProps.FadeTexture) + new Vector2(1f, 0f);
			material.SetColor(Allin1ShaderProps.OverlayColor, discardGlowColor);
			material.SetColor(Allin1ShaderProps.FadeBurnColor, discardGlowColor);
			_discardSequence.Append(material.DOFloat(1f, Allin1ShaderProps.FadeAmount, duration2));
			_discardSequence.Join(material.DOOffset(endValue2, Allin1ShaderProps.FadeTexture, duration2));
			_discardSequence.Join(Transform.DOPunchRotation(Vector3.forward * discardPunchAngle, duration2, discardPunchVibrato).SetEase(easeFunction));
			_discardSequence.Join(material.DOFloat(discardOverlayBlendMaxAmount, Allin1ShaderProps.OverlayBlend, duration2));
			_discardSequence.Join(material.DOFloat(discardOverlayGlowAmount, Allin1ShaderProps.OverlayGlow, duration2));
			_discardSequence.Join(Transform.DOScale(endValue, duration2));
			_discardSequence.Join(StartDiscardFadeParticleSystem(duration2));
			_discardSequence.SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
			try
			{
				await _discardSequence.ToUniTask(TweenCancelBehaviour.KillAndCancelAwait, cancellationToken);
			}
			finally
			{
				_discardSequence = null;
			}
		}

		public async UniTask PlayDiscardDragFadeAnimation(float duration, Transform targetTransform, CancellationToken cancellationToken = default(CancellationToken))
		{
			Material material = Card3DProxy?.Get2DMaterial();
			if (!material)
			{
				return;
			}
			_discardSequence?.Kill();
			_discardSequence = DOTween.Sequence(this);
			SetRenderOrderTopMost();
			EnableSelectionOuterGlow(state: false);
			float discardPunchAngle = animationSettings.DiscardPunchAngle;
			int discardPunchVibrato = animationSettings.DiscardPunchVibrato;
			EaseFunction easeFunction = animationSettings.DiscardPunchEase.GetEaseFunction();
			Vector3 endValue = viewHandler.GlobalScale * animationSettings.DiscardScaleMultiplier;
			Color32 discardGlowColor = animationSettings.DiscardGlowColor;
			float discardOverlayBlendMaxAmount = animationSettings.DiscardOverlayBlendMaxAmount;
			float discardOverlayGlowAmount = animationSettings.DiscardOverlayGlowAmount;
			float num = material.GetFloat(Allin1ShaderProps.FadeAmount);
			float num2 = duration * Mathf.Abs(1f - num) / 1.1f;
			Vector2 endValue2 = material.GetTextureOffset(Allin1ShaderProps.FadeTexture) + new Vector2(1f, 0f);
			material.SetColor(Allin1ShaderProps.OverlayColor, discardGlowColor);
			material.SetColor(Allin1ShaderProps.FadeBurnColor, discardGlowColor);
			_discardSequence.Append(material.DOFloat(1f, Allin1ShaderProps.FadeAmount, num2));
			_discardSequence.Join(material.DOOffset(endValue2, Allin1ShaderProps.FadeTexture, num2));
			_discardSequence.Join(Transform.DOPunchRotation(Vector3.forward * discardPunchAngle, num2, discardPunchVibrato).SetEase(easeFunction));
			_discardSequence.Join(material.DOFloat(discardOverlayBlendMaxAmount, Allin1ShaderProps.OverlayBlend, num2));
			_discardSequence.Join(material.DOFloat(discardOverlayGlowAmount, Allin1ShaderProps.OverlayGlow, num2));
			_discardSequence.Join(Transform.DOScale(endValue, num2));
			_discardSequence.Join(Transform.DOMove(targetTransform.position, num2 / 3f));
			_discardSequence.Join(StartDiscardFadeParticleSystem(num2));
			_discardSequence.SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
			try
			{
				await _discardSequence.ToUniTask(TweenCancelBehaviour.KillAndCancelAwait, cancellationToken);
			}
			finally
			{
				_discardSequence = null;
			}
		}

		public void StopDiscardFadeAnimation(float duration)
		{
			Material material = Card3DProxy?.Get2DMaterial();
			if ((bool)material)
			{
				_discardSequence?.Kill();
				_discardSequence = DOTween.Sequence(this);
				float num = material.GetFloat(Allin1ShaderProps.FadeAmount);
				float duration2 = duration * Mathf.Abs(-0.1f - num) / 1.1f;
				_discardSequence.Append(material.DOFloat(-0.1f, Allin1ShaderProps.FadeAmount, duration2));
				_discardSequence.Join(material.DOFloat(0f, Allin1ShaderProps.OverlayBlend, duration2));
				_discardSequence.Join(StopDiscardFadeParticleSystem(duration2));
				_discardSequence.SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
				SetRenderOrderToDefault();
				EnableSelectionOuterGlow(state: true);
			}
		}

		private Tween StartDiscardFadeParticleSystem(float duration)
		{
			discardFadeParticleSystem.Play();
			discardFadeParticleSystem.StartEmission();
			return DiscardFadeParticleSystem.rectTransform.DOAnchorPosY(DiscardParticleSystemMaxHeight, duration).SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true).OnComplete(discardFadeParticleSystem.StopEmission);
		}

		private Tween StopDiscardFadeParticleSystem(float duration)
		{
			discardFadeParticleSystem.StopEmission();
			return DiscardFadeParticleSystem.rectTransform.DOAnchorPosY(DiscardParticleSystemMinHeight, duration).SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
		}

		public async UniTask PlayDiscardExplosionParticleSystem()
		{
			DiscardExplosionParticleSystem.Play();
			DiscardExplosionParticleSystem.StartEmission();
			await UniTask.NextFrame(base.destroyCancellationToken, cancelImmediately: true);
			await UniTask.WaitUntil(() => DiscardExplosionParticleSystem.particles.All((ParticleSystem element) => !element.IsAlive(withChildren: true)), PlayerLoopTiming.Update, base.destroyCancellationToken, cancelImmediately: true);
		}

		public async UniTask PlayBanishFadeAnimation(float duration, CancellationToken cancellationToken = default(CancellationToken))
		{
			Material material = Card3DProxy?.Get2DMaterial();
			if (!material)
			{
				return;
			}
			_banishSequence?.Kill();
			_banishSequence = DOTween.Sequence(this);
			SetRenderOrderTopMost();
			EnableSelectionOuterGlow(state: false);
			float banishPunchAngle = animationSettings.BanishPunchAngle;
			int banishPunchVibrato = animationSettings.BanishPunchVibrato;
			EaseFunction easeFunction = animationSettings.BanishPunchEase.GetEaseFunction();
			Vector3 endValue = viewHandler.GlobalScale * animationSettings.BanishScaleMultiplier;
			Color32 banishGlowColor = animationSettings.BanishGlowColor;
			float banishOverlayBlendMaxAmount = animationSettings.BanishOverlayBlendMaxAmount;
			float banishOverlayGlowAmount = animationSettings.BanishOverlayGlowAmount;
			float num = material.GetFloat(Allin1ShaderProps.FadeAmount);
			float duration2 = duration * Mathf.Abs(1f - num) / 1.1f;
			Vector2 endValue2 = material.GetTextureOffset(Allin1ShaderProps.FadeTexture) + new Vector2(1f, 0f);
			material.SetColor(Allin1ShaderProps.OverlayColor, banishGlowColor);
			material.SetColor(Allin1ShaderProps.FadeBurnColor, banishGlowColor);
			_banishSequence.Append(material.DOFloat(1f, Allin1ShaderProps.FadeAmount, duration2));
			_banishSequence.Join(material.DOOffset(endValue2, Allin1ShaderProps.FadeTexture, duration2));
			_banishSequence.Join(Transform.DOPunchRotation(Vector3.forward * banishPunchAngle, duration2, banishPunchVibrato).SetEase(easeFunction));
			_banishSequence.Join(material.DOFloat(banishOverlayBlendMaxAmount, Allin1ShaderProps.OverlayBlend, duration2));
			_banishSequence.Join(material.DOFloat(banishOverlayGlowAmount, Allin1ShaderProps.OverlayGlow, duration2));
			_banishSequence.Join(Transform.DOScale(endValue, duration2));
			_banishSequence.Join(StartBanishFadeParticleSystem(duration2));
			_banishSequence.SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
			try
			{
				await _banishSequence.ToUniTask(TweenCancelBehaviour.KillAndCancelAwait, cancellationToken);
			}
			finally
			{
				_banishSequence = null;
			}
		}

		public async UniTask PlayBanishDragFadeAnimation(float duration, Transform targetTransform, CancellationToken cancellationToken = default(CancellationToken))
		{
			Material material = Card3DProxy?.Get2DMaterial();
			if (!material)
			{
				return;
			}
			_banishSequence?.Kill();
			_banishSequence = DOTween.Sequence(this);
			SetRenderOrderTopMost();
			EnableSelectionOuterGlow(state: false);
			float banishPunchAngle = animationSettings.BanishPunchAngle;
			int banishPunchVibrato = animationSettings.BanishPunchVibrato;
			EaseFunction easeFunction = animationSettings.BanishPunchEase.GetEaseFunction();
			Vector3 endValue = viewHandler.GlobalScale * animationSettings.BanishScaleMultiplier;
			Color32 banishGlowColor = animationSettings.BanishGlowColor;
			float banishOverlayBlendMaxAmount = animationSettings.BanishOverlayBlendMaxAmount;
			float banishOverlayGlowAmount = animationSettings.BanishOverlayGlowAmount;
			float num = material.GetFloat(Allin1ShaderProps.FadeAmount);
			float num2 = duration * Mathf.Abs(1f - num) / 1.1f;
			Vector2 endValue2 = material.GetTextureOffset(Allin1ShaderProps.FadeTexture) + new Vector2(1f, 0f);
			material.SetColor(Allin1ShaderProps.OverlayColor, banishGlowColor);
			material.SetColor(Allin1ShaderProps.FadeBurnColor, banishGlowColor);
			_banishSequence.Append(material.DOFloat(1f, Allin1ShaderProps.FadeAmount, num2));
			_banishSequence.Join(material.DOOffset(endValue2, Allin1ShaderProps.FadeTexture, num2));
			_banishSequence.Join(Transform.DOPunchRotation(Vector3.forward * banishPunchAngle, num2, banishPunchVibrato).SetEase(easeFunction));
			_banishSequence.Join(material.DOFloat(banishOverlayBlendMaxAmount, Allin1ShaderProps.OverlayBlend, num2));
			_banishSequence.Join(material.DOFloat(banishOverlayGlowAmount, Allin1ShaderProps.OverlayGlow, num2));
			_banishSequence.Join(Transform.DOScale(endValue, num2));
			_banishSequence.Join(Transform.DOMove(targetTransform.position, num2 / 3f));
			_banishSequence.Join(StartBanishFadeParticleSystem(num2));
			_banishSequence.SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
			try
			{
				await _banishSequence.ToUniTask(TweenCancelBehaviour.KillAndCancelAwait, cancellationToken);
			}
			finally
			{
				_banishSequence = null;
			}
		}

		public void StopBanishFadeAnimation(float duration)
		{
			Material material = Card3DProxy?.Get2DMaterial();
			if ((bool)material)
			{
				_banishSequence?.Kill();
				_banishSequence = DOTween.Sequence(this);
				float num = material.GetFloat(Allin1ShaderProps.FadeAmount);
				float duration2 = duration * Mathf.Abs(-0.1f - num) / 1.1f;
				_banishSequence.Append(material.DOFloat(-0.1f, Allin1ShaderProps.FadeAmount, duration2));
				_banishSequence.Join(material.DOFloat(0f, Allin1ShaderProps.OverlayBlend, duration2));
				_banishSequence.Join(StopBanishFadeParticleSystem(duration2));
				_banishSequence.SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
				SetRenderOrderToDefault();
				EnableSelectionOuterGlow(state: true);
			}
		}

		private Tween StartBanishFadeParticleSystem(float duration)
		{
			banishFadeParticleSystem.Play();
			banishFadeParticleSystem.StartEmission();
			return BanishFadeParticleSystem.rectTransform.DOAnchorPosY(BanishParticleSystemMaxHeight, duration).SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true).OnComplete(banishFadeParticleSystem.StopEmission);
		}

		private Tween StopBanishFadeParticleSystem(float duration)
		{
			banishFadeParticleSystem.StopEmission();
			return BanishFadeParticleSystem.rectTransform.DOAnchorPosY(BanishParticleSystemMinHeight, duration).SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
		}

		public async UniTask PlayBanishExplosionParticleSystem()
		{
			BanishExplosionParticleSystem.Play();
			BanishExplosionParticleSystem.StartEmission();
			await UniTask.NextFrame(base.destroyCancellationToken, cancelImmediately: true);
			await UniTask.WaitUntil(() => BanishExplosionParticleSystem.particles.All((ParticleSystem element) => !element.IsAlive(withChildren: true)), PlayerLoopTiming.Update, base.destroyCancellationToken, cancelImmediately: true);
		}

		public void ShowWeaponCompatVFX(bool state)
		{
			if (state)
			{
				Color color = animationSettings.WeaponCompatibilityColor;
				float weaponCompatibilityMinFadeAmount = animationSettings.WeaponCompatibilityMinFadeAmount;
				float weaponCompatibilityMaxFadeAmount = animationSettings.WeaponCompatibilityMaxFadeAmount;
				float weaponCompatibilityFadeTime = animationSettings.WeaponCompatibilityFadeTime;
				float weaponCompatibilityBlinkTime = animationSettings.WeaponCompatibilityBlinkTime;
				HideAllOuterGlows();
				EnableWeaponCompatGlow(state: true);
				StartContextualInnerGlowVFX(color, weaponCompatibilityMinFadeAmount, weaponCompatibilityMaxFadeAmount, weaponCompatibilityFadeTime, weaponCompatibilityBlinkTime);
			}
			else
			{
				EnableWeaponCompatGlow(state: false);
				StopContextualInnerGlowVFX();
			}
		}

		public void EnableWeaponCompatGlow(bool state)
		{
			if ((bool)weaponCompatGlow)
			{
				if (!weaponCompatGlow.texture)
				{
					weaponCompatGlow.texture = UICardRenderingManager.Instance.GetCardDynamicTexture(viewHandler);
				}
				float weaponCompatibilityFadeTime = animationSettings.WeaponCompatibilityFadeTime;
				_weaponCompatibilityTween?.Kill();
				_weaponCompatibilityTween = DOTween.Sequence(this);
				_weaponCompatibilityTween.Append(weaponCompatGlow.DOFade(state ? 1 : 0, weaponCompatibilityFadeTime));
				_weaponCompatibilityTween.SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
			}
		}

		public void ShowUnCompatVFX(bool state)
		{
			Material material = Card3DProxy?.Get2DMaterial();
			if ((bool)material && (bool)selectionGlow)
			{
				ShowWeaponCompatVFX(state: false);
				_weaponUnCompatibilityTween?.Kill();
				_weaponUnCompatibilityTween = DOTween.Sequence(this);
				float weaponCompatibilityFadeTime = animationSettings.WeaponCompatibilityFadeTime;
				if (state)
				{
					_weaponUnCompatibilityTween.Append(material.DOFloat(1f, Allin1ShaderProps.GreyScaleBlend, weaponCompatibilityFadeTime));
					_weaponUnCompatibilityTween.Join(selectionGlow.materialForRendering.DOFloat(1f, Allin1ShaderProps.GreyScaleBlend, weaponCompatibilityFadeTime));
					_weaponUnCompatibilityTween.Join(selectionGlow.materialForRendering.DOFloat(0f, Allin1ShaderProps.GradientBlend, weaponCompatibilityFadeTime));
				}
				else
				{
					_weaponUnCompatibilityTween.Append(material.DOFloat(0f, Allin1ShaderProps.GreyScaleBlend, weaponCompatibilityFadeTime));
					_weaponUnCompatibilityTween.Join(selectionGlow.materialForRendering.DOFloat(0f, Allin1ShaderProps.GreyScaleBlend, weaponCompatibilityFadeTime));
					_weaponUnCompatibilityTween.Join(selectionGlow.materialForRendering.DOFloat(1f, Allin1ShaderProps.GradientBlend, weaponCompatibilityFadeTime));
				}
				_weaponUnCompatibilityTween.SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
			}
		}

		public void ShowMergeCompatVFX(bool state)
		{
			if (state)
			{
				float mergeCompatibilityFadeTime = animationSettings.MergeCompatibilityFadeTime;
				float mergeCompatibilityBlinkTime = animationSettings.MergeCompatibilityBlinkTime;
				Color color = animationSettings.MergeCompatibilityColor;
				float mergeCompatibilityMinFadeAmount = animationSettings.MergeCompatibilityMinFadeAmount;
				float mergeCompatibilityMaxFadeAmount = animationSettings.MergeCompatibilityMaxFadeAmount;
				HideAllOuterGlows();
				EnableMergeOuterGlow(state: true);
				StartContextualInnerGlowVFX(color, mergeCompatibilityMinFadeAmount, mergeCompatibilityMaxFadeAmount, mergeCompatibilityFadeTime, mergeCompatibilityBlinkTime);
			}
			else
			{
				EnableMergeOuterGlow(state: false);
				StopContextualInnerGlowVFX();
			}
		}

		public void HideCompatVFX()
		{
			ShowWeaponCompatVFX(state: false);
			ShowMergeCompatVFX(state: false);
		}

		public void HideUnCompatVFX()
		{
			ShowUnCompatVFX(state: false);
		}

		public void EnableRarityVFX(bool state, bool instant = false)
		{
			_rarityFadeTween?.Kill();
			_rarityScaleBlinkTween?.Kill();
			float rarityGlowScaleAmount = animationSettings.RarityGlowScaleAmount;
			if (instant)
			{
				if (state)
				{
					silverRarityGlow.alpha = 1f;
					silverRarityGlowImage.transform.localScale = Vector3.one * rarityGlowScaleAmount;
					goldRarityGlow.alpha = 1f;
					goldRarityGlowImage.transform.localScale = Vector3.one * rarityGlowScaleAmount;
					prismRarityGlow.alpha = 1f;
					prismRarityGlowImage.transform.localScale = Vector3.one * rarityGlowScaleAmount;
				}
				else
				{
					silverRarityGlow.alpha = 0f;
					goldRarityGlow.alpha = 0f;
					prismRarityGlow.alpha = 0f;
				}
				return;
			}
			_rarityFadeTween = DOTween.Sequence(this);
			_rarityScaleBlinkTween = DOTween.Sequence(this);
			if (state)
			{
				float rarityGlowScaleTime = animationSettings.RarityGlowScaleTime;
				EaseFunction easeFunction = animationSettings.RarityGlowScaleEase.GetEaseFunction();
				silverRarityGlowImage.transform.localScale = Vector3.one;
				_rarityFadeTween.Append(silverRarityGlow.DOFade(1f, animationSettings.RarityGlowFadeTime).SetEase(animationSettings.RarityGlowFadeEase.GetEaseFunction()));
				_rarityScaleBlinkTween.Append(silverRarityGlowImage.transform.DOScale(rarityGlowScaleAmount, rarityGlowScaleTime).SetEase(easeFunction));
				_rarityFadeTween.Join(goldRarityGlow.DOFade(1f, animationSettings.RarityGlowFadeTime)).SetEase(animationSettings.RarityGlowFadeEase.GetEaseFunction());
				goldRarityGlowImage.transform.localScale = Vector3.one;
				_rarityScaleBlinkTween.Join(goldRarityGlowImage.transform.DOScale(rarityGlowScaleAmount, rarityGlowScaleTime).SetEase(easeFunction));
				_rarityFadeTween.Join(prismRarityGlow.DOFade(1f, animationSettings.RarityGlowFadeTime)).SetEase(animationSettings.RarityGlowFadeEase.GetEaseFunction());
				prismRarityGlowImage.transform.localScale = Vector3.one;
				_rarityScaleBlinkTween.Join(prismRarityGlowImage.transform.DOScale(rarityGlowScaleAmount, rarityGlowScaleTime).SetEase(easeFunction));
			}
			else
			{
				_rarityFadeTween.Append(silverRarityGlow.DOFade(0f, animationSettings.RarityGlowFadeTime)).SetEase(animationSettings.RarityGlowFadeEase.GetEaseFunction());
				_rarityFadeTween.Join(goldRarityGlow.DOFade(0f, animationSettings.RarityGlowFadeTime)).SetEase(animationSettings.RarityGlowFadeEase.GetEaseFunction());
				_rarityFadeTween.Join(prismRarityGlow.DOFade(0f, animationSettings.RarityGlowFadeTime)).SetEase(animationSettings.RarityGlowFadeEase.GetEaseFunction());
			}
			_rarityFadeTween.SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
			_rarityScaleBlinkTween.SetLoops(-1, LoopType.Yoyo);
			_rarityScaleBlinkTween.SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
		}

		public void SetWeaponRarity(WeaponRarity rarity)
		{
			switch (rarity)
			{
			case WeaponRarity.Silver:
				silverRarityGlow.gameObject.SetActive(value: true);
				goldRarityGlow.gameObject.SetActive(value: false);
				prismRarityGlow.gameObject.SetActive(value: false);
				break;
			case WeaponRarity.Gold:
				silverRarityGlow.gameObject.SetActive(value: false);
				goldRarityGlow.gameObject.SetActive(value: true);
				prismRarityGlow.gameObject.SetActive(value: false);
				break;
			case WeaponRarity.Holographic:
				silverRarityGlow.gameObject.SetActive(value: false);
				goldRarityGlow.gameObject.SetActive(value: false);
				prismRarityGlow.gameObject.SetActive(value: true);
				break;
			default:
				silverRarityGlow.gameObject.SetActive(value: false);
				goldRarityGlow.gameObject.SetActive(value: false);
				prismRarityGlow.gameObject.SetActive(value: false);
				break;
			}
			silverRarityGlow.alpha = 0f;
			goldRarityGlow.alpha = 0f;
			prismRarityGlow.alpha = 0f;
		}

		public void SetEquipmentRarity(uint levelIndex)
		{
			switch (levelIndex)
			{
			case 0u:
				silverRarityGlow.gameObject.SetActive(value: false);
				goldRarityGlow.gameObject.SetActive(value: false);
				prismRarityGlow.gameObject.SetActive(value: false);
				break;
			case 1u:
				silverRarityGlow.gameObject.SetActive(value: true);
				goldRarityGlow.gameObject.SetActive(value: false);
				prismRarityGlow.gameObject.SetActive(value: false);
				break;
			case 2u:
				silverRarityGlow.gameObject.SetActive(value: false);
				goldRarityGlow.gameObject.SetActive(value: true);
				prismRarityGlow.gameObject.SetActive(value: false);
				break;
			default:
				silverRarityGlow.gameObject.SetActive(value: false);
				goldRarityGlow.gameObject.SetActive(value: true);
				prismRarityGlow.gameObject.SetActive(value: false);
				break;
			}
			silverRarityGlow.alpha = 0f;
			goldRarityGlow.alpha = 0f;
			prismRarityGlow.alpha = 0f;
		}
	}
}
