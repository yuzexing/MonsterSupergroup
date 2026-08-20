using System;
using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI.HUD
{
	public class MinimapIcon : MonoBehaviour
	{
		public enum PingMode
		{
			None = 0,
			Once = 1,
			Continuous = 2
		}

		[SerializeField]
		private RectTransform arrowRectTransform;

		[SerializeField]
		private Image icon;

		[SerializeField]
		private UIIconPing iconPing;

		private MinimapUIManager _minimapUIManager;

		private Transform _iconTarget;

		private RectTransform _parentRectTransform;

		private bool _isActive;

		private Vector2 _offset;

		private float _size;

		private MinimapUIManager.MinimapIconType _iconType;

		private PingMode _pingMode;

		private bool _isPinging;

		private Action _pingAction;

		private float DistanceToFrame => _parentRectTransform.rect.height / 2f;

		private bool IsInitialized { get; set; }

		public void Init()
		{
			_parentRectTransform = base.transform.parent.GetComponent<RectTransform>();
			_minimapUIManager = MinimapUIManager.Instance;
			ResetPosition();
			if (iconPing != null)
			{
				iconPing.PulseStarted -= PlayPingSound;
				iconPing.PulseStarted += PlayPingSound;
			}
			_isActive = false;
			IsInitialized = true;
			MinimapUIManager.Instance?.RegisterIcon(this);
		}

		public void OnDestroy()
		{
			if (iconPing != null)
			{
				iconPing.PulseStarted -= PlayPingSound;
			}
			CancelPing();
			MinimapUIManager.Instance?.UnRegisterIcon(this);
		}

		public void SetTarget(Transform target)
		{
			_iconTarget = target;
		}

		public void SetIcon(Sprite sprite)
		{
			if (!(sprite == null))
			{
				icon.sprite = sprite;
			}
		}

		public void SetIconType(MinimapUIManager.MinimapIconType iconType)
		{
			_iconType = iconType;
		}

		private void PlayPingSound(int pingNumber)
		{
			if (_iconType != MinimapUIManager.MinimapIconType.None)
			{
				MinimapUIManager minimapUIManager = ((_minimapUIManager != null) ? _minimapUIManager : MinimapUIManager.Instance);
				if (!(minimapUIManager == null))
				{
					minimapUIManager.PlayPingSound(_iconType, pingNumber);
				}
			}
		}

		public void SetSize(float size)
		{
			_size = size;
			icon.rectTransform.localScale = new Vector3(size, size, 1f);
		}

		private void ResetPosition()
		{
			MoveCalc();
			ApplyMovement();
		}

		public void Release()
		{
			CancelPing();
			IsInitialized = false;
			_isActive = false;
			MinimapUIManager.Instance?.ReturnToPool(this);
		}

		public void OnUpdate()
		{
			if (IsInitialized || !base.gameObject.activeInHierarchy)
			{
				if (_isActive)
				{
					OffScreenCheck();
					MoveCalc();
					ApplyMovement();
				}
				else
				{
					OnScreenCheck();
				}
			}
		}

		private void MoveCalc()
		{
			_offset = (_iconTarget.position - _minimapUIManager.FollowTarget.position) / _minimapUIManager.HeightInUnits * _parentRectTransform.rect.width;
		}

		private void ApplyMovement()
		{
			arrowRectTransform.anchoredPosition = new Vector2(_offset.x, _offset.y);
		}

		private void OnScreenCheck()
		{
			if (((Vector2)(_iconTarget.position - _minimapUIManager.FollowTarget.position)).magnitude < _minimapUIManager.HeightInUnits / 2f)
			{
				Show();
			}
		}

		private void OffScreenCheck()
		{
			if (((Vector2)(_iconTarget.position - _minimapUIManager.FollowTarget.position)).magnitude > _minimapUIManager.HeightInUnits / 2f)
			{
				Hide();
			}
		}

		public void Show()
		{
			_isActive = true;
			icon.gameObject.SetActive(value: true);
			_pingAction?.Invoke();
			_pingAction = null;
		}

		public void Hide()
		{
			_isActive = false;
			icon.gameObject.SetActive(value: false);
			CancelPing();
		}

		public void SetPing(PingMode mode)
		{
			_pingMode = mode;
			if (_pingMode == PingMode.Continuous)
			{
				_pingAction = RunContinuousPing;
			}
			else
			{
				_pingAction = RunPingOnce;
			}
		}

		private async void RunPingOnce()
		{
			try
			{
				if (!_isPinging)
				{
					_isPinging = true;
					await iconPing.RunPingOnce();
					_isPinging = false;
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		private void RunContinuousPing()
		{
			_isPinging = true;
			iconPing.RunContinuousPing();
		}

		private void CancelPing()
		{
			iconPing?.CancelPing();
			_isPinging = false;
		}
	}
}
