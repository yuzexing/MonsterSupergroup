using AstralShift.Helpers;
using AstralShift.QTI.Helpers;
using Coffee.UIEffects;
using Com.LuisPedroFonseca.ProCamera2D;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI.Cards
{
	public class WSMCardSlotView : ViewFollower
	{
		[Header("References")]
		[SerializeField]
		protected WSMCardSlotViewHandler viewHandler;

		[SerializeField]
		protected Canvas canvas;

		[SerializeField]
		protected int topMostSortingOrder;

		[SerializeField]
		protected WSMCardSlot3DView slot3DView;

		[SerializeField]
		protected UIGeneric3DRenderTarget uiRenderTarget;

		[Space]
		[SerializeField]
		private Transform shakeParent;

		[Header("Outer Glow VFX")]
		[SerializeField]
		private RawImage outerGlowEffect;

		[SerializeField]
		private UIEffect outerGlowUIEffect;

		private ColorFilter _outerGlowEffectDefaultColorFilter;

		private Transform _transform;

		private bool _rotationFollow = true;

		private bool _canTilt;

		private bool _allowMovement = true;

		private bool _allowIdleFloat = true;

		private float _idleOffset;

		private bool _lockAllMotion;

		private Vector3 _rotationSmoothDelta;

		private Vector3 _movementSmoothDelta;

		private float _containerRotationDelta;

		private float _followTargetRotationInfluence;

		private Tween _idleOffsetTween;

		private int _idleOffsetSign = 1;

		private Tween _hoverRotateTween;

		private Tween _hoverScaleTween;

		private Tween _selectionOuterGlowTween;

		private Vector3 _previousScale;

		private Vector3 _nextScale;

		private float _scaleSensibilityTimeout;

		public Canvas Canvas => canvas;

		public WSMCardSlot3DView Slot3DView
		{
			get
			{
				if (!slot3DView)
				{
					slot3DView = UIRenderTarget.Renderer?.GetComponent<WSMCardSlot3DView>();
				}
				return slot3DView;
			}
		}

		public UIGeneric3DRenderTarget UIRenderTarget => uiRenderTarget;

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
			outerGlowEffect.color = new Color(1f, 1f, 1f, 0f);
			if ((bool)outerGlowUIEffect)
			{
				_outerGlowEffectDefaultColorFilter = outerGlowUIEffect.colorFilter;
			}
		}

		public void Init(WSMCardSlotViewHandler viewHandler)
		{
			this.viewHandler = viewHandler;
			uiRenderTarget.Init();
		}

		public UniTask InitAsync(WSMCardSlotViewHandler viewHandler)
		{
			this.viewHandler = viewHandler;
			return uiRenderTarget.InitAsync();
		}

		public void Dispose()
		{
			Hide();
			UICardRenderingManager.Instance.UnRegisterRenderer(UIRenderTarget);
			Object.Destroy(base.gameObject);
		}

		protected void OnDestroy()
		{
			Hide();
			if ((bool)UIRenderTarget)
			{
				UICardRenderingManager.Instance.UnRegisterRenderer(UIRenderTarget);
				Object.Destroy(UIRenderTarget);
			}
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

		public void EnableStaticRender(bool state)
		{
			Slot3DView.Renderer.CanBeStatic = state;
		}

		protected override void LateUpdate()
		{
			if (!viewHandler)
			{
				return;
			}
			if (_lockAllMotion)
			{
				ApplyViewPortPositionTo3DView();
				return;
			}
			ApplyViewPortPositionTo3DView();
			SmoothScaleFollow();
			if (_allowMovement)
			{
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
				Transform.localScale = viewHandler.transform.lossyScale / ((viewHandler.Canvas != null) ? viewHandler.Canvas.scaleFactor : 1f);
			}
		}

		public void InitIdleAnimation()
		{
			float resAdjustedScreenSpaceOffset = UIResolutionHelpers.GetResAdjustedScreenSpaceOffset(animationSettings.IdleMoveOffset);
			RefreshIdleAnimation();
			_idleOffsetTween = DOTween.To(() => _idleOffset, delegate(float value)
			{
				_idleOffset = (float)_idleOffsetSign * value;
			}, resAdjustedScreenSpaceOffset, animationSettings.IdleMoveTime).SetEase(animationSettings.IdleMoveOffsetEase.GetEaseFunction()).SetLoops(-1, LoopType.Yoyo)
				.SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
		}

		public void RefreshIdleAnimation()
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
			if (_canTilt && (bool)Slot3DView)
			{
				float hoverTiltAmount = animationSettings.HoverTiltAmount;
				float hoverTiltSpeed = animationSettings.HoverTiltSpeed;
				Vector2 direction = input;
				if (isPosition)
				{
					Vector2 vector = ProCamera2D.Instance.GameCamera.ScreenToViewportPoint(Transform.position);
					direction = (Vector2)ProCamera2D.Instance.GameCamera.ScreenToViewportPoint(input) - vector;
				}
				Slot3DView.ApplyTilt(direction, hoverTiltAmount, hoverTiltSpeed);
			}
		}

		public void ApplyTilt(Vector2 direction, float magnitude)
		{
			if (_canTilt && (bool)Slot3DView)
			{
				float hoverTiltSpeed = animationSettings.HoverTiltSpeed;
				Slot3DView.ApplyTilt(direction.normalized, magnitude, hoverTiltSpeed);
			}
		}

		public void EnableTilt()
		{
			_canTilt = true;
		}

		public void StopTilt()
		{
			Slot3DView.StopTilt(animationSettings.HoverTiltStopSpeed);
		}

		public void DisableTilt(bool instant = false)
		{
			_canTilt = false;
			if (instant)
			{
				Slot3DView.StopTiltInstant();
			}
			else
			{
				Slot3DView.StopTilt(animationSettings.HoverTiltStopSpeed);
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

		public void EnableSelectionOuterGlow(bool state)
		{
			float hoverGlowFadeTime = animationSettings.HoverGlowFadeTime;
			Color endValue = animationSettings.HoverGlowColor;
			if (!outerGlowEffect.texture)
			{
				outerGlowEffect.texture = UICardRenderingManager.Instance.GetGenericDynamicTexture(UIRenderTarget);
			}
			outerGlowUIEffect.colorFilter = _outerGlowEffectDefaultColorFilter;
			if (outerGlowUIEffect.colorFilter == ColorFilter.HsvModifier)
			{
				endValue = animationSettings.HoverGlowColorHSV;
			}
			outerGlowEffect.DOFade(state ? 1 : 0, hoverGlowFadeTime).SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
			_selectionOuterGlowTween?.Kill();
			_selectionOuterGlowTween = DOTween.To(() => outerGlowUIEffect.color, delegate(Color value)
			{
				outerGlowUIEffect.color = value;
			}, endValue, hoverGlowFadeTime).SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
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
			Vector3 vector = ProCamera2D.Instance.GameCamera.ScreenToViewportPoint(Transform.position);
			Vector3 vector2 = ProCamera2D.Instance.GameCamera.ScreenToViewportPoint(followTransform.position);
			Vector3 vector3 = vector - vector2;
			_movementSmoothDelta = Vector3.Lerp(_movementSmoothDelta, vector3, animationSettings.FollowRotationSpeed * Time.unscaledDeltaTime);
			Vector3 a = _movementSmoothDelta * animationSettings.FollowRotationAmount;
			a = Vector3.Lerp(a, vector3 * animationSettings.FollowRotationAmount, Time.unscaledDeltaTime * animationSettings.FollowRotationSpeed);
			_followTargetRotationInfluence += Time.unscaledDeltaTime * animationSettings.FollowRotationSpeed;
			_followTargetRotationInfluence = Mathf.Clamp01(_followTargetRotationInfluence);
			_rotationSmoothDelta = Vector3.Lerp(_rotationSmoothDelta, a, animationSettings.FollowRotationSpeed * Time.unscaledDeltaTime);
			float z = followTransform.eulerAngles.z;
			z = ((z > 180f) ? (z - 360f) : z);
			float z2 = Mathf.Lerp(Mathf.Clamp(_rotationSmoothDelta.x, 0f - animationSettings.FollowRotationMaxAngle, animationSettings.FollowRotationMaxAngle), z, _followTargetRotationInfluence);
			Vector3 eulerAngles = Transform.eulerAngles;
			Quaternion rotation = Quaternion.Euler(eulerAngles.x, eulerAngles.y, z2);
			Transform.rotation = rotation;
		}

		public void ApplyViewPortPositionTo3DView()
		{
			if ((bool)Slot3DView)
			{
				_ = (Vector2)ProCamera2D.Instance.GameCamera.ScreenToViewportPoint(Transform.position);
			}
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

		public Tween MotionBlur(float startDistance, float endDistance, float localAngleDegrees, float duration)
		{
			float value = Math.Remap(localAngleDegrees, -360f, 360f, -1f, 1f);
			UIRenderTarget.GetMaterial().SetFloat(Allin1ShaderProps.MotionBlurDistance, startDistance);
			UIRenderTarget.GetMaterial().SetFloat(Allin1ShaderProps.MotionBlurAngle, value);
			return UIRenderTarget.GetMaterial().DOFloat(endDistance, Allin1ShaderProps.MotionBlurDistance, duration);
		}

		public void EnableMotionBlur(bool state)
		{
			if (state)
			{
				UIRenderTarget.GetMaterial().EnableKeyword(Allin1ShaderProps.MotionBlurOn);
			}
			else
			{
				UIRenderTarget.GetMaterial().DisableKeyword(Allin1ShaderProps.MotionBlurOn);
			}
		}
	}
}
