using DG.Tweening;
using UnityEngine;

namespace AstralShift.HellMaiden.UI.Cards
{
	[RequireComponent(typeof(UIGeneric3DRenderer))]
	public class WSMCardSlot3DView : MonoBehaviour
	{
		[SerializeField]
		protected UIGeneric3DRenderer _renderer;

		protected Transform _transform;

		private float _staticTimer;

		private Tween _rotateTween;

		private Tween _stopTiltTween;

		private Tween _tiltTween;

		public UIGeneric3DRenderer Renderer => _renderer;

		public Transform Transform
		{
			get
			{
				_transform = base.transform;
				return _transform;
			}
		}

		private void Reset()
		{
			TryGetComponent<UIGeneric3DRenderer>(out _renderer);
		}

		private void Awake()
		{
			TryGetComponent<UIGeneric3DRenderer>(out _renderer);
		}

		public Tween RotateOnPlaceEffect(float duration, float angle)
		{
			_stopTiltTween?.Kill();
			_tiltTween?.Kill();
			_rotateTween?.Kill();
			_rotateTween = Transform.DOLocalRotate(new Vector3(0f, angle, 0f), duration, RotateMode.FastBeyond360);
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
			_tiltTween = ShortcutExtensions.DOLocalRotate(endValue: new Vector3(direction.y * amount, (0f - direction.x) * amount, 0f), target: Transform, duration: duration, mode: RotateMode.FastBeyond360).SetUpdate(UpdateType.Late, isIndependentUpdate: true);
			return _tiltTween;
		}

		public Tween StopTilt(float speed, bool instant = false)
		{
			_tiltTween?.Kill();
			_stopTiltTween?.Kill();
			_stopTiltTween = Transform.DOLocalRotate(Vector3.zero, speed).SetSpeedBased(isSpeedBased: true).SetUpdate(UpdateType.Late, isIndependentUpdate: true);
			return _stopTiltTween;
		}

		public void StopTiltInstant()
		{
			_tiltTween?.Kill();
			_stopTiltTween?.Kill();
			SetRotation(0f);
		}
	}
}
