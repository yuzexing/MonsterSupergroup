using UnityEngine;

namespace AstralShift.HellMaiden.UI
{
	public class UIGeneric3DRenderer : MonoBehaviour
	{
		protected UIGeneric3DRenderTarget target;

		protected Transform _transform;

		protected Vector2 _currentPosition;

		protected Vector2 _lastPosition = Vector2.one;

		protected Quaternion _currentRotation;

		protected Quaternion _lastRotation = Quaternion.identity;

		protected bool _isStatic;

		protected bool _canBeStatic = true;

		protected float _staticTimer;

		protected const float StaticTimeThreshold = 0.66f;

		protected const float StaticPositionThresholdSqr = 0.001f;

		protected const float StaticRotationAngleThreshold = 0.1f;

		public Transform Transform
		{
			get
			{
				_transform = base.transform;
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

		public void Init(UIGeneric3DRenderTarget renderTarget)
		{
			target = renderTarget;
		}

		public void TryRender(Camera camera)
		{
			if (!IsStatic)
			{
				Render(camera);
			}
			EvaluateIfIsStatic();
		}

		public void Render(Camera camera, RenderTexture texture)
		{
			EnableVisibility(state: true);
			Matrix4x4 projectionMatrix = camera.projectionMatrix;
			Matrix4x4 worldToCameraMatrix = camera.worldToCameraMatrix;
			camera.targetTexture = texture;
			camera.worldToCameraMatrix = target.GetCameraWorldToCameraMatrix();
			camera.projectionMatrix = target.GetCameraProjectionMatrix();
			camera.Render();
			camera.targetTexture = null;
			camera.projectionMatrix = projectionMatrix;
			camera.worldToCameraMatrix = worldToCameraMatrix;
			EnableVisibility(state: false);
		}

		public void Render(Camera camera)
		{
			EnableVisibility(state: true);
			Matrix4x4 projectionMatrix = camera.projectionMatrix;
			Matrix4x4 worldToCameraMatrix = camera.worldToCameraMatrix;
			camera.targetTexture = target.RenderTexture;
			camera.worldToCameraMatrix = target.GetCameraWorldToCameraMatrix();
			camera.projectionMatrix = target.GetCameraProjectionMatrix();
			camera.Render();
			camera.targetTexture = null;
			camera.projectionMatrix = projectionMatrix;
			camera.worldToCameraMatrix = worldToCameraMatrix;
			EnableVisibility(state: false);
		}

		private void EvaluateIfIsStatic()
		{
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
	}
}
