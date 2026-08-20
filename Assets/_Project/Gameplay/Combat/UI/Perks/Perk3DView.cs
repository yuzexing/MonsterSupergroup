using System.Collections.Generic;
using AstralShift.Helpers;
using DG.Tweening;
using UnityEngine;

namespace AstralShift.HellMaiden.UI.Perks
{
	public class Perk3DView : MonoBehaviour
	{
		[SerializeField]
		private MeshRenderer iconRenderer;

		[SerializeField]
		private int iconMaterialIndex;

		[SerializeField]
		[HideInInspector]
		protected List<Renderer> _renderers;

		private RenderTexture _renderTexture;

		private Vector2 _currentPosition;

		private Vector2 _lastPosition = Vector2.one;

		private Quaternion _currentRotation;

		private Quaternion _lastRotation = Quaternion.identity;

		private Vector3 _localEulerRotationOffset;

		private bool _isStatic;

		private bool _canBeStatic = true;

		private float _staticTimer;

		private const float StaticTimeThreshold = 0.66f;

		private const float StaticPositionThresholdSqr = 0.0001f;

		private const float StaticRotationAngleThreshold = 0.1f;

		[SerializeField]
		protected Transform _transform;

		[SerializeField]
		protected Transform _idleRotationTransform;

		[SerializeField]
		protected float depthOffset;

		private readonly int _mainTexPropID = Shader.PropertyToID("_BaseMap");

		private readonly int ViewPortPositionShaderPropID = Shader.PropertyToID("_ViewPortPosition");

		private Tween _rotateTween;

		private Tween _stopTiltTween;

		private Tween _tiltTween;

		public List<Renderer> Renderers => _renderers;

		public RenderTexture RenderTexture => _renderTexture;

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
				if (!_canBeStatic)
				{
					_isStatic = false;
				}
			}
		}

		public void Initialize()
		{
			TryGetComponent<Transform>(out _transform);
		}

		public void AssignTexture(RenderTexture texture)
		{
			_renderTexture = texture;
		}

		public void TryRender(Camera camera)
		{
			if (!IsStatic)
			{
				Render(camera);
			}
			if (CanBeStatic)
			{
				EvaluateIfIsStatic();
			}
		}

		public void Render(Camera camera, RenderTexture texture)
		{
			EnableVisibility(state: true);
			camera.targetTexture = texture;
			camera.Render();
			EnableVisibility(state: false);
		}

		public void Render(Camera camera)
		{
			EnableVisibility(state: true);
			camera.targetTexture = RenderTexture;
			camera.Render();
			EnableVisibility(state: false);
		}

		private void EvaluateIfIsStatic()
		{
			float sqrMagnitude = (_currentPosition - _lastPosition).sqrMagnitude;
			float num = Quaternion.Angle(_currentRotation, _lastRotation);
			_currentRotation = base.transform.localRotation;
			if (sqrMagnitude > 0.0001f || num > 0.1f)
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
			SetLayerRecursive(_transform, state ? 30 : 0);
		}

		private void SetLayerRecursive(Transform transform, int layer)
		{
			transform.gameObject.layer = layer;
			foreach (Transform item in transform)
			{
				SetLayerRecursive(item, layer);
			}
		}

		public void SetIcon(Sprite sprite)
		{
			if ((bool)iconRenderer)
			{
				SpriteHelpers.SetTextureWithAtlasSupport(sprite, iconRenderer.materials[iconMaterialIndex], _mainTexPropID);
				TryAddToRenderersList(iconRenderer);
			}
		}

		public void TryAddToRenderersList(MeshRenderer renderer)
		{
			if (!_renderers.Contains(renderer))
			{
				_renderers.Add(renderer);
			}
		}

		public Tween RotateOnPlaceEffect(float duration, float angle)
		{
			_stopTiltTween?.Kill();
			_tiltTween?.Kill();
			_rotateTween?.Kill();
			_rotateTween = _transform.DOLocalRotate(new Vector3(0f, angle, 0f), duration, RotateMode.FastBeyond360);
			if (angle >= 360f)
			{
				_rotateTween.OnComplete(delegate
				{
					_transform.localEulerAngles = new Vector3(0f, _transform.localEulerAngles.y - 360f, 0f);
				});
			}
			if (angle <= -360f)
			{
				_rotateTween.OnComplete(delegate
				{
					_transform.localEulerAngles = new Vector3(0f, _transform.localEulerAngles.y + 360f, 0f);
				});
			}
			return _rotateTween;
		}

		public void SetRotation(float angle)
		{
			_stopTiltTween?.Kill();
			_tiltTween?.Kill();
			_rotateTween?.Kill();
			_transform.localEulerAngles = new Vector3(0f, angle, 0f);
		}

		public void ApplyRotationOffset(Vector3 offset)
		{
			_idleRotationTransform.localEulerAngles = offset;
		}

		public Tween VerticalPunch(float force, float duration, int vibrato, float elasticity)
		{
			return _transform.DOPunchPosition(Vector3.up * force, duration, vibrato, elasticity);
		}

		public void ApplyTilt(Vector2 direction, float amount, float speed, bool accumulateRotation = false)
		{
			_tiltTween?.Kill();
			_stopTiltTween?.Kill();
			float b = direction.y * amount;
			float b2 = (0f - direction.x) * amount;
			Vector3 localEulerAngles = base.transform.localEulerAngles;
			float x = Mathf.LerpAngle(localEulerAngles.x, b, speed * Time.unscaledDeltaTime);
			float y = Mathf.LerpAngle(localEulerAngles.y, b2, speed * Time.unscaledDeltaTime);
			localEulerAngles = new Vector3(x, y, 0f);
			base.transform.localEulerAngles = localEulerAngles;
		}

		public Tween Tilt(Vector3 direction, float amount, float duration)
		{
			_stopTiltTween?.Kill();
			_tiltTween?.Kill();
			_tiltTween = ShortcutExtensions.DOLocalRotate(endValue: new Vector3(direction.y * amount, (0f - direction.x) * amount, 0f), target: base.transform, duration: duration, mode: RotateMode.FastBeyond360).SetUpdate(UpdateType.Late, isIndependentUpdate: true);
			return _tiltTween;
		}

		public Tween StopTilt(float speed, bool instant = false)
		{
			_tiltTween?.Kill();
			_stopTiltTween?.Kill();
			_stopTiltTween = base.transform.DOLocalRotate(Vector3.zero, speed).SetSpeedBased(isSpeedBased: true).SetUpdate(UpdateType.Late, isIndependentUpdate: true);
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
			_transform.localPosition = new Vector3(0f, 0f, depthOffset);
			ApplyShaderPropertyToMeshRenderers(ViewPortPositionShaderPropID, position);
		}

		private void ApplyShaderPropertyToMeshRenderers(int propID, Vector4 value)
		{
			for (int i = 0; i < Renderers.Count; i++)
			{
				Material[] materials = Renderers[i].materials;
				foreach (Material material in materials)
				{
					if (material.HasProperty(propID))
					{
						material.SetVector(propID, value);
					}
				}
			}
		}
	}
}
