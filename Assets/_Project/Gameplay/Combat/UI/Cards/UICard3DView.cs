using System;
using DG.Tweening;
using UnityEngine;

namespace AstralShift.HellMaiden.UI.Cards
{
	public class UICard3DView : Card3DView
	{
		private UICard3DProxy _card3DProxy;

		private RenderTexture _renderTexture;

		[Space]
		[SerializeField]
		protected Transform rotationOffsetTransform;

		protected Transform _transform;

		private Vector2 _currentPosition;

		private Vector2 _lastPosition = Vector2.one;

		private Quaternion _currentRotation;

		private Quaternion _lastRotation = Quaternion.identity;

		private bool _isStatic;

		private bool _canBeStatic = true;

		private bool _forceStatic;

		private bool _renderQueued;

		private float _staticTimer;

		private const float StaticTimeThreshold = 0.66f;

		private const float StaticPositionThresholdSqr = 0.001f;

		private const float StaticRotationAngleThreshold = 0.1f;

		private Action OnInitializedCallback;

		private Action OnDestroyCallback;

		private Tween _rotateTween;

		private Tween _stopTiltTween;

		private Tween _tiltTween;

		private readonly int ViewPortPositionShaderPropID = Shader.PropertyToID("_ViewPortPosition");

		public UICard3DProxy Card3DProxy => _card3DProxy;

		public RenderTexture RenderTexture => _renderTexture;

		public Transform Transform
		{
			get
			{
				if (!_transform)
				{
					if (!base.transform)
					{
						return null;
					}
					_transform = base.transform;
				}
				return _transform;
			}
		}

		public bool IsStatic => _isStatic;

		public bool CanBeStatic
		{
			get
			{
				return _canBeStatic;
			}
			set
			{
				_canBeStatic = value;
			}
		}

		public bool ForceStatic
		{
			get
			{
				return _forceStatic;
			}
			set
			{
				_forceStatic = value;
			}
		}

		public void Initialize(UICard3DProxy card3DProxy)
		{
			_card3DProxy = card3DProxy;
			TryGetComponent<Transform>(out _transform);
			OnInitializedCallback?.Invoke();
		}

		protected virtual void OnDestroy()
		{
			DOTween.Kill(this);
			OnDestroyCallback?.Invoke();
		}

		public void AssignTexture(RenderTexture texture)
		{
			_renderTexture = texture;
		}

		public void EnqueueRender()
		{
			UICardRenderingManager.Instance.EnqueueRender(this);
		}

		public void TryRender(Camera camera)
		{
			if (!IsStatic || _renderQueued)
			{
				_renderQueued = false;
				Render(camera);
			}
			EvaluateIfIsStatic();
		}

		public void RenderStatic(Camera camera, RenderTexture[] textures, bool renderOnlyDynamicRes = false)
		{
			EnableVisibility(state: true);
			Matrix4x4 projectionMatrix = camera.projectionMatrix;
			Matrix4x4 worldToCameraMatrix = camera.worldToCameraMatrix;
			camera.worldToCameraMatrix = UICardRenderingManager.Instance.GetStaticCardWorldToCameraMatrix(this);
			camera.projectionMatrix = UICardRenderingManager.Instance.GetStaticCardCameraProjectionMatrix();
			camera.targetTexture = textures[0];
			camera.Render();
			if (renderOnlyDynamicRes)
			{
				camera.targetTexture = null;
				camera.projectionMatrix = projectionMatrix;
				camera.worldToCameraMatrix = worldToCameraMatrix;
				EnableVisibility(state: false);
			}
			else
			{
				camera.targetTexture = textures[1];
				camera.Render();
				camera.projectionMatrix = projectionMatrix;
				camera.worldToCameraMatrix = worldToCameraMatrix;
				EnableVisibility(state: false);
				Graphics.Blit(textures[0], textures[2], UICardRenderingManager.Instance.BlitHalfResMaterial, 0);
			}
		}

		public void Render(Camera camera)
		{
			EnableVisibility(state: true);
			Matrix4x4 projectionMatrix = camera.projectionMatrix;
			Matrix4x4 worldToCameraMatrix = camera.worldToCameraMatrix;
			camera.worldToCameraMatrix = Card3DProxy.GetWorldToCameraMatrix();
			camera.projectionMatrix = Card3DProxy.GetCameraProjectionMatrix();
			camera.targetTexture = RenderTexture;
			camera.Render();
			camera.targetTexture = null;
			camera.projectionMatrix = projectionMatrix;
			camera.worldToCameraMatrix = worldToCameraMatrix;
			EnableVisibility(state: false);
		}

		private void EvaluateIfIsStatic()
		{
			if (ForceStatic)
			{
				_isStatic = true;
				return;
			}
			if (!CanBeStatic)
			{
				_isStatic = false;
				return;
			}
			float sqrMagnitude = (_currentPosition - _lastPosition).sqrMagnitude;
			float num = Quaternion.Angle(_currentRotation, _lastRotation);
			_currentRotation = Transform.localRotation;
			if (sqrMagnitude > 0.001f || num > 0.1f)
			{
				_isStatic = false;
				_staticTimer = 0f;
				_lastPosition = _currentPosition;
				_lastRotation = _currentRotation;
				return;
			}
			_staticTimer += Time.unscaledDeltaTime;
			if (!(_staticTimer < 0.66f))
			{
				_isStatic = true;
				_staticTimer = 0.66f;
			}
		}

		public void EnableVisibility(bool state)
		{
			SetLayerRecursive(Transform, state ? 31 : 0);
		}

		private void SetLayerRecursive(Transform transform, int layer)
		{
			transform.gameObject.layer = layer;
			foreach (Transform item in transform)
			{
				SetLayerRecursive(item, layer);
			}
		}

		public Tween RotateOnPlaceEffect(float duration, float angle)
		{
			_stopTiltTween?.Kill();
			_tiltTween?.Kill();
			_rotateTween?.Kill();
			_rotateTween = Transform.DOLocalRotate(new Vector3(0f, angle, 0f), duration, RotateMode.FastBeyond360);
			_rotateTween.SetTarget(this);
			if (angle >= 360f)
			{
				_rotateTween.OnComplete(delegate
				{
					Transform.localEulerAngles = new Vector3(0f, Transform.localEulerAngles.y - 360f, 0f);
				});
			}
			if (angle <= -360f)
			{
				_rotateTween.OnComplete(delegate
				{
					Transform.localEulerAngles = new Vector3(0f, Transform.localEulerAngles.y + 360f, 0f);
				});
			}
			return _rotateTween;
		}

		public void SetRotation(float angle)
		{
			_stopTiltTween?.Kill();
			_tiltTween?.Kill();
			_rotateTween?.Kill();
			Transform.localEulerAngles = new Vector3(0f, angle, 0f);
		}

		public void ApplyRotationOffset(Vector3 offset)
		{
			rotationOffsetTransform.localEulerAngles = offset;
		}

		public void ApplyTilt(Vector2 direction, float amount, float speed)
		{
			_tiltTween?.Kill();
			_stopTiltTween?.Kill();
			float b = direction.y * amount;
			float b2 = (0f - direction.x) * amount;
			Vector3 localEulerAngles = Transform.localEulerAngles;
			float x = Mathf.LerpAngle(localEulerAngles.x, b, speed * Time.unscaledDeltaTime);
			float y = Mathf.LerpAngle(localEulerAngles.y, b2, speed * Time.unscaledDeltaTime);
			localEulerAngles = new Vector3(x, y, 0f);
			Transform.localEulerAngles = localEulerAngles;
		}

		public Tween Tilt(Vector3 direction, float amount, float duration)
		{
			_stopTiltTween?.Kill();
			_tiltTween?.Kill();

			_tiltTween = rotationOffsetTransform
				.DOLocalRotate(
					new Vector3(
						direction.y * amount,
						-direction.x * amount,
						0f),
					duration,
					RotateMode.FastBeyond360)
				.SetUpdate(UpdateType.Late, isIndependentUpdate: true);

			_tiltTween.SetTarget(this);
			_tiltTween.SetLink(base.gameObject);

			return _tiltTween;
		}

		public Tween StopTilt(float speed, bool instant = false)
		{
			_tiltTween?.Kill();
			_stopTiltTween?.Kill();
			_stopTiltTween = Transform?.DOLocalRotate(Vector3.zero, speed).SetSpeedBased(isSpeedBased: true).SetUpdate(UpdateType.Late, isIndependentUpdate: true);
			_stopTiltTween.SetTarget(this);
			_stopTiltTween.SetLink(base.gameObject);
			return _stopTiltTween;
		}

		public void StopTiltInstant()
		{
			_tiltTween?.Kill();
			_stopTiltTween?.Kill();
			SetRotation(0f);
		}

		public void SetViewPortPosition(Vector2 position)
		{
			_currentPosition = position;
			ApplyShaderPropertyToMeshRenderers(ViewPortPositionShaderPropID, position);
		}

		private void ApplyShaderPropertyToMeshRenderers(int propID, float value)
		{
			for (int i = 0; i < base.Renderers.Count; i++)
			{
				Renderer renderer = base.Renderers[i];
				if (renderer.material.HasProperty(propID))
				{
					renderer.material.SetFloat(propID, value);
				}
			}
		}

		private void ApplyShaderPropertyToMeshRenderers(int propID, Vector4 value)
		{
			for (int i = 0; i < base.Renderers.Count; i++)
			{
				Renderer renderer = base.Renderers[i];
				if (renderer.material.HasProperty(propID))
				{
					renderer.material.SetVector(propID, value);
				}
			}
		}
	}
}
