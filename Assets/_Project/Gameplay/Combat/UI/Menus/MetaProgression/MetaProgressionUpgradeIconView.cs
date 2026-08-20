using Com.LuisPedroFonseca.ProCamera2D;
using DG.Tweening;
using UnityEngine;

namespace AstralShift.HellMaiden.UI.Menus.MetaProgression
{
	public class MetaProgressionUpgradeIconView : MonoBehaviour
	{
		[SerializeField]
		protected UIGeneric3DRenderTarget defaultRenderTarget;

		[SerializeField]
		protected UIGeneric3DRenderTarget maxedOutRenderTarget;

		[SerializeField]
		protected CanvasGroup canvasGroup;

		[SerializeField]
		protected float fadeDuration = 0.1f;

		private MetaProgressionUpgrade3DIcon _defaultInstance;

		private MetaProgressionUpgrade3DIcon _maxedOutInstance;

		private bool _isMaxedOut;

		private Tween _fadeTween;

		private Vector3 _previousRotation;

		[SerializeField]
		private Vector3 _rotation;

		[SerializeField]
		private bool CanBeStatic = true;

		public void Initialize(MetaProgressionUpgrade3DIcon defaultInstance, MetaProgressionUpgrade3DIcon maxedOutInstance)
		{
			_isMaxedOut = false;
			_defaultInstance = defaultInstance;
			_maxedOutInstance = maxedOutInstance;
			defaultRenderTarget.Init(_defaultInstance);
			defaultRenderTarget.RawImage.color = Color.white;
			maxedOutRenderTarget.Init(_maxedOutInstance);
			maxedOutRenderTarget.RawImage.color = Color.clear;
			canvasGroup.alpha = 0f;
			ApplyViewPortPositionTo3DView();
		}

		public void Upgrade()
		{
			_isMaxedOut = true;
			defaultRenderTarget.RawImage.color = Color.clear;
			maxedOutRenderTarget.RawImage.color = Color.white;
		}

		public void Downgrade()
		{
			_isMaxedOut = false;
			defaultRenderTarget.RawImage.color = Color.white;
			maxedOutRenderTarget.RawImage.color = Color.clear;
		}

		public void Release()
		{
			defaultRenderTarget.Release();
			maxedOutRenderTarget.Release();
			_defaultInstance = null;
			_maxedOutInstance = null;
		}

		public void Rotate(Vector3 eulerAngles)
		{
			if ((bool)_defaultInstance && (bool)_maxedOutInstance)
			{
				_defaultInstance.Rotate(eulerAngles);
				_maxedOutInstance.Rotate(eulerAngles);
			}
		}

		private void ApplyViewPortPositionTo3DView()
		{
			if ((bool)_defaultInstance && (bool)_maxedOutInstance)
			{
				Vector2 position = ProCamera2D.Instance.GameCamera.ScreenToViewportPoint(base.transform.position);
				_defaultInstance.SetViewPortPosition(position);
				_maxedOutInstance.SetViewPortPosition(position);
			}
		}

		public void Show()
		{
			_fadeTween?.Kill();
			_fadeTween = canvasGroup.DOFade(1f, fadeDuration).SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
		}

		public void Hide(bool instant = false)
		{
			if (instant)
			{
				_fadeTween?.Kill();
				canvasGroup.alpha = 0f;
			}
			else
			{
				_fadeTween?.Kill();
				_fadeTween = canvasGroup.DOFade(0f, fadeDuration).SetUpdate(DG.Tweening.UpdateType.Late, isIndependentUpdate: true);
			}
		}

		public RenderTexture GetRenderTexture()
		{
			if (!_isMaxedOut)
			{
				return defaultRenderTarget.RenderTexture;
			}
			return maxedOutRenderTarget.RenderTexture;
		}

		private void OnDidApplyAnimationProperties()
		{
			if (_previousRotation != _rotation)
			{
				_previousRotation = _rotation;
				Rotate(_rotation);
				ApplyViewPortPositionTo3DView();
			}
		}
	}
}
